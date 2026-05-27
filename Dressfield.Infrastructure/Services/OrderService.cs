using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Core.Enums;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly DressfieldDbContext _db;
    private readonly IPaymentService _payment;
    private readonly ICartService _cart;
    private readonly ILogger<OrderService> _logger;
    private readonly decimal _shippingCost;
    private readonly string _paymentPageBaseUrl;

    public OrderService(DressfieldDbContext db, IPaymentService payment, ICartService cart, IConfiguration configuration, ILogger<OrderService> logger)
    {
        _db = db;
        _payment = payment;
        _cart = cart;
        _logger = logger;
        _shippingCost = decimal.TryParse(configuration["Orders:ShippingCost"], out var sc) ? sc : 5m;
        _paymentPageBaseUrl = configuration["BogIPay:PaymentPageBaseUrl"] ?? "https://payment.bog.ge/";
    }

    public async Task<IReadOnlyCollection<OrderSummaryDto>> GetAdminAsync(OrderStatus? status)
    {
        var query = _db.Orders.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.UserId,
                o.ContactName,
                o.ContactEmail,
                o.ContactPhone,
                o.ShippingCity,
                o.Status,
                o.TotalAmount,
                o.Items.Count,
                o.CreatedAt))
            .ToListAsync();
    }

    public async Task<OrderDetailDto?> GetAdminByIdAsync(int id)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? null : MapDetail(order);
    }

    public async Task UpdateStatusAsync(int id, UpdateOrderStatusRequest request, string? changedByUserId = null)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new KeyNotFoundException("შეკვეთა ვერ მოიძებნა");

        var previousStatus = order.Status;
        order.Status = request.Status;
        order.AdminNotes = request.AdminNotes?.Trim();
        order.UpdatedAt = DateTime.UtcNow;

        // Persist tracking info when transitioning to Shipped (or updating it later)
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
            order.TrackingNumber = request.TrackingNumber.Trim();
        if (!string.IsNullOrWhiteSpace(request.TrackingUrl))
            order.TrackingUrl = request.TrackingUrl.Trim();

        _db.OrderStatusLogs.Add(new OrderStatusLog
        {
            OrderId = order.Id,
            FromStatus = previousStatus,
            ToStatus = request.Status,
            ChangedByUserId = changedByUserId,
            Notes = request.AdminNotes?.Trim(),
        });

        if (request.Status == OrderStatus.Shipped && !string.IsNullOrEmpty(order.ContactEmail))
            QueueShippingEmail(order);

        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyCollection<OrderSummaryDto>> GetByUserAsync(string userId)
    {
        return await _db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.UserId,
                o.ContactName,
                o.ContactEmail,
                o.ContactPhone,
                o.ShippingCity,
                o.Status,
                o.TotalAmount,
                o.Items.Count,
                o.CreatedAt))
            .ToListAsync();
    }

    public async Task<OrderDetailDto?> GetByIdForUserAsync(int id, string userId)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        return order is null ? null : MapDetail(order);
    }

    public async Task<OrderStatusLookupDto?> GetPublicStatusAsync(int orderId, string orderKey)
    {
        var normalizedKey = orderKey.Trim();

        return await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.BogOrderKey == normalizedKey)
            .Select(o => new OrderStatusLookupDto(
                o.Id,
                o.Status,
                o.UpdatedAt,
                o.TrackingNumber,
                o.TrackingUrl))
            .FirstOrDefaultAsync();
    }

    public async Task<CheckoutResponse> CreateAsync(CreateOrderRequest request, string? userId, string? idempotencyKey = null)
    {
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        if (normalizedIdempotencyKey is not null && !string.IsNullOrEmpty(userId))
        {
            var existing = await _db.Orders
                .AsNoTracking()
                .Where(o => o.UserId == userId && o.IdempotencyKey == normalizedIdempotencyKey)
                .Select(o => new { o.Id, o.Status, o.BogOrderId })
                .FirstOrDefaultAsync();

            if (existing is not null)
            {
                var redirectUrl = existing.Status == OrderStatus.AwaitingPayment && !string.IsNullOrWhiteSpace(existing.BogOrderId)
                    ? BuildBogRedirectUrl(existing.BogOrderId)
                    : null;

                return new CheckoutResponse(existing.Id, redirectUrl, existing.Status != OrderStatus.Cancelled && redirectUrl is not null);
            }
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("ერთ-ერთი პროდუქტი მიუწვდომელია.");

        var items = new List<OrderItem>();
        var stockReservations = new List<(int VariantId, int Quantity, string ProductName)>();
        decimal subtotal = 0;

        foreach (var cartItem in request.Items)
        {
            var product = products.First(p => p.Id == cartItem.ProductId);
            var variant = cartItem.VariantId.HasValue
                ? product.Variants.FirstOrDefault(v => v.Id == cartItem.VariantId.Value && v.IsActive)
                : null;

            if (cartItem.VariantId.HasValue)
            {
                if (variant is null)
                    throw new InvalidOperationException($"არჩეული ვარიანტი მიუწვდომელია: {product.Name}");
                if (variant.StockQuantity < cartItem.Quantity)
                    throw new InvalidOperationException($"მარაგი არასაკმარისია: {product.Name}");

                stockReservations.Add((variant.Id, cartItem.Quantity, product.Name));
            }

            var discountedBasePrice = PricingHelper.CalculateDiscountedPrice(product.BasePrice, product.SalePercentage);
            var unitPrice = discountedBasePrice + (variant?.PriceAdjustment ?? 0);
            var lineTotal = unitPrice * cartItem.Quantity;
            subtotal += lineTotal;

            items.Add(new OrderItem
            {
                ProductId = product.Id,
                VariantId = variant?.Id,
                ProductName = product.Name,
                ProductSlug = product.Slug,
                ProductImageUrl = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.ImageUrl,
                VariantName = variant != null ? $"{variant.Name}: {variant.Value}" : null,
                UnitPrice = unitPrice,
                Quantity = cartItem.Quantity,
                LineTotal = lineTotal
            });
        }

        var normalizedPromoCode = NormalizePromoCode(request.PromoCode);
        decimal promoDiscountAmount = 0m;
        decimal? promoDiscountPercentage = null;
        int? promoCodeId = null;

        if (!string.IsNullOrWhiteSpace(normalizedPromoCode))
        {
            var promoCode = await _db.PromoCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == normalizedPromoCode);

            if (promoCode is null || !promoCode.IsActive)
                throw new InvalidOperationException("პრომო კოდი არასწორია ან გამორთულია.");

            if (promoCode.ExpiresAtUtc.HasValue && promoCode.ExpiresAtUtc.Value <= DateTime.UtcNow)
                throw new InvalidOperationException("პრომო კოდის ვადა გასულია.");

            if (promoCode.MaxUsesPerUser.HasValue && !string.IsNullOrEmpty(userId))
            {
                var userUses = await _db.Orders
                    .CountAsync(o => o.UserId == userId
                                 && o.PromoCode == normalizedPromoCode
                                 && o.Status != OrderStatus.Cancelled);
                if (userUses >= promoCode.MaxUsesPerUser.Value)
                    throw new InvalidOperationException("პრომო კოდის გამოყენების ლიმიტი ამოწურულია.");
            }

            promoCodeId = promoCode.Id;
            promoDiscountPercentage = PricingHelper.NormalizePercent(promoCode.DiscountPercentage);
            promoDiscountAmount = PricingHelper.RoundMoney(subtotal * promoDiscountPercentage.Value / 100m);
        }

        var discountedSubtotal = Math.Max(0m, subtotal - promoDiscountAmount);
        var orderKey = Guid.NewGuid().ToString("N");

        var order = new Order
        {
            UserId = userId,
            IdempotencyKey = !string.IsNullOrEmpty(userId) ? normalizedIdempotencyKey : null,
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            ShippingCity = request.ShippingCity.Trim(),
            ShippingAddressLine1 = request.ShippingAddressLine1.Trim(),
            ShippingAddressLine2 = request.ShippingAddressLine2?.Trim(),
            ShippingPostalCode = request.ShippingPostalCode?.Trim(),
            CustomerNotes = request.CustomerNotes?.Trim(),
            Subtotal = subtotal,
            PromoDiscountAmount = promoDiscountAmount,
            PromoDiscountPercentage = promoDiscountPercentage,
            PromoCode = normalizedPromoCode,
            ShippingCost = _shippingCost,
            TotalAmount = discountedSubtotal + _shippingCost,
            BogOrderKey = orderKey,
            Status = OrderStatus.Pending,
            Items = items
        };

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Atomic stock decrement — guard prevents oversell under concurrent checkouts
        foreach (var (variantId, qty, productName) in stockReservations)
        {
            var updated = await _db.ProductVariants
                .Where(v => v.Id == variantId && v.StockQuantity >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockQuantity, v => v.StockQuantity - qty));

            if (updated == 0)
                throw new InvalidOperationException($"მარაგი არასაკმარისია: {productName}");
        }

        // Atomic promo-code usage claim
        if (promoCodeId.HasValue)
        {
            var claimed = await _db.PromoCodes
                .Where(p => p.Id == promoCodeId.Value
                         && p.IsActive
                         && (p.MaxUses == null || p.UsedCount < p.MaxUses))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));

            if (claimed == 0)
                throw new InvalidOperationException("პრომო კოდის ლიმიტი ამოწურულია.");
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var description = $"DressField შეკვეთა #{order.Id}";
        var paymentResult = await _payment.CreateSessionAsync(order.Id, order.TotalAmount, orderKey, description);

        if (paymentResult.Success && paymentResult.BogOrderId != null)
        {
            order.BogOrderId = paymentResult.BogOrderId;
            order.Status = OrderStatus.AwaitingPayment;
            await _db.SaveChangesAsync();
        }
        else
        {
            // Compensate: cancel the order, restore stock/promo, log the failure
            await CancelAndRestoreAsync(order, stockReservations, promoCodeId, paymentResult.ErrorMessage);
        }

        return new CheckoutResponse(
            order.Id,
            paymentResult.RedirectUrl,
            paymentResult.Success);
    }

    public async Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey)
    {
        // Atomic claim: only one concurrent caller transitions AwaitingPayment → PaymentProcessing.
        // RSA signature on the controller already authenticates the caller; orderKey is not re-checked here.
        var claimed = await _db.Orders
            .Where(o => o.BogOrderId == bogOrderId && o.Status == OrderStatus.AwaitingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.PaymentProcessing)
                                      .SetProperty(o => o.UpdatedAt, DateTime.UtcNow));

        if (claimed == 0)
        {
            _logger.LogInformation("Callback for {BogOrderId} not claimable (already handled or unknown)", bogOrderId);
            return;
        }

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstAsync(o => o.BogOrderId == bogOrderId);

        var result = await _payment.VerifyCallbackAsync(bogOrderId);

        var currencyOk = string.Equals(result.Currency, "GEL", StringComparison.OrdinalIgnoreCase);
        var amountOk = result.VerifiedAmount.HasValue
                       && Math.Abs(result.VerifiedAmount.Value - order.TotalAmount) <= 0.01m;

        if (result.IsApproved && (!currencyOk || !amountOk))
        {
            var reason = BuildMismatchReason(order.TotalAmount, result, currencyOk, amountOk);

            _logger.LogCritical(
                "BOG payment verification failed for order {OrderId} — {Reason} (BOG: {BogOrderId})",
                order.Id, reason, bogOrderId);

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            _db.OrderStatusLogs.Add(new OrderStatusLog
            {
                OrderId = order.Id,
                FromStatus = OrderStatus.PaymentProcessing,
                ToStatus = OrderStatus.Cancelled,
                Notes = reason,
            });

            await RestoreStockAndPromoAsync(order);
            await _db.SaveChangesAsync();
            return;
        }

        if (BogPaymentStatus.IsPending(result.Status))
        {
            order.Status = OrderStatus.AwaitingPayment;
            order.UpdatedAt = DateTime.UtcNow;

            _db.OrderStatusLogs.Add(new OrderStatusLog
            {
                OrderId = order.Id,
                FromStatus = OrderStatus.PaymentProcessing,
                ToStatus = OrderStatus.AwaitingPayment,
                ChangedByUserId = null,
                Notes = $"BOG callback not terminal yet: {result.Status}",
            });

            await _db.SaveChangesAsync();
            return;
        }

        order.Status    = result.IsApproved ? OrderStatus.Paid : OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        _db.OrderStatusLogs.Add(new OrderStatusLog
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.PaymentProcessing,
            ToStatus = order.Status,
            ChangedByUserId = null,
            Notes = $"BOG callback: {(result.IsApproved ? "approved" : "declined")} (txn: {result.TransactionId})",
        });

        if (result.IsApproved)
        {
            if (!string.IsNullOrEmpty(order.ContactEmail))
            {
                var itemsHtml = string.Join("", order.Items.Select(i =>
                {
                    var name = System.Net.WebUtility.HtmlEncode(i.ProductName);
                    var variant = i.VariantName != null ? $" ({System.Net.WebUtility.HtmlEncode(i.VariantName)})" : "";
                    return $"<tr><td style=\"padding:6px 0;\">{name}{variant}</td>" +
                           $"<td style=\"padding:6px 0;text-align:center;\">{i.Quantity}</td>" +
                           $"<td style=\"padding:6px 0;text-align:right;\">₾{i.LineTotal:F2}</td></tr>";
                }));
                QueueConfirmationEmail(order.ContactEmail, order.Id, itemsHtml, $"₾{order.TotalAmount:F2}");
            }

            // Clear the cart for authenticated users so it doesn't show stale items
            if (!string.IsNullOrEmpty(order.UserId))
            {
                try
                {
                    await _cart.ClearCartAsync(order.UserId);
                }
                catch (Exception ex)
                {
                    // Non-critical — order is already confirmed; log and continue
                    _logger.LogError(ex, "Failed to clear cart for user {UserId} after order {OrderId} payment", order.UserId, order.Id);
                }
            }
        }
        else
        {
            await RestoreStockAndPromoAsync(order);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Order {OrderId} payment {Result} (BOG: {BogOrderId})",
            order.Id, result.IsApproved ? "approved" : "declined", bogOrderId);
    }

    private async Task CancelAndRestoreAsync(
        Order order,
        List<(int VariantId, int Quantity, string ProductName)> stockReservations,
        int? promoCodeId,
        string? error)
    {
        var previous = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        var notes = $"BOG session creation failed: {error}";
        _db.OrderStatusLogs.Add(new OrderStatusLog
        {
            OrderId = order.Id,
            FromStatus = previous,
            ToStatus = OrderStatus.Cancelled,
            ChangedByUserId = null,
            Notes = notes.Length > 1000 ? notes[..1000] : notes,
        });

        foreach (var (variantId, qty, _) in stockReservations)
        {
            await _db.ProductVariants
                .Where(v => v.Id == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockQuantity, v => v.StockQuantity + qty));
        }

        if (promoCodeId.HasValue)
        {
            await _db.PromoCodes
                .Where(p => p.Id == promoCodeId.Value && p.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1));
        }

        await _db.SaveChangesAsync();
    }

    private async Task RestoreStockAndPromoAsync(Order order)
    {
        foreach (var item in order.Items.Where(i => i.VariantId.HasValue))
        {
            var variantId = item.VariantId!.Value;
            var qty = item.Quantity;
            await _db.ProductVariants
                .Where(v => v.Id == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockQuantity, v => v.StockQuantity + qty));
        }

        if (!string.IsNullOrEmpty(order.PromoCode))
        {
            await _db.PromoCodes
                .Where(p => p.Code == order.PromoCode && p.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1));
        }
    }

    private static OrderDetailDto MapDetail(Order order) =>
        new(
            order.Id,
            order.UserId,
            order.ContactName,
            order.ContactEmail,
            order.ContactPhone,
            order.ShippingCity,
            order.ShippingAddressLine1,
            order.ShippingAddressLine2,
            order.ShippingPostalCode,
            order.Status,
            order.Subtotal,
            order.PromoDiscountAmount,
            order.PromoDiscountPercentage,
            order.PromoCode,
            order.ShippingCost,
            order.TotalAmount,
            order.TrackingNumber,
            order.TrackingUrl,
            order.CustomerNotes,
            order.AdminNotes,
            order.BogOrderId,
            order.CreatedAt,
            order.UpdatedAt,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDto(
                    i.Id,
                    i.ProductId,
                    i.ProductName,
                    i.ProductSlug,
                    i.ProductImageUrl,
                    i.VariantName,
                    i.UnitPrice,
                    i.Quantity,
                    i.LineTotal))
                .ToList());

    private void QueueConfirmationEmail(string to, int orderId, string itemsHtml, string total)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#333;">
                <h2 style="margin-bottom:4px;">გადახდა წარმატებით შესრულდა!</h2>
                <p style="color:#888;margin-top:0;">შეკვეთის ნომერი: <strong>#{orderId}</strong></p>

                <table style="width:100%;border-collapse:collapse;margin:16px 0;">
                    <thead>
                        <tr style="border-bottom:2px solid #eee;text-align:left;font-size:13px;color:#888;">
                            <th style="padding:8px 0;">პროდუქტი</th>
                            <th style="padding:8px 0;text-align:center;">რ-ბა</th>
                            <th style="padding:8px 0;text-align:right;">ფასი</th>
                        </tr>
                    </thead>
                    <tbody>{itemsHtml}</tbody>
                </table>

                <p style="font-size:16px;font-weight:600;text-align:right;margin:16px 0;">სულ: {total}</p>

                <div style="background:#f9f9f9;border-radius:8px;padding:16px;margin:16px 0;">
                    <p style="margin:0 0 4px;font-size:14px;font-weight:600;">რა ხდება შემდეგ?</p>
                    <p style="margin:0;font-size:13px;color:#666;">ჩვენ მოვამზადებთ თქვენს შეკვეთას და გამოგზავნისას მიიღებთ შეტყობინებას ტრეკინგ ნომრით.</p>
                </div>

                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;margin:0;">გმადლობთ, რომ აირჩიეთ DressField!</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = $"შეკვეთა #{orderId} დადასტურებულია — DressField",
            HtmlBody = html,
        });
    }

    private void QueueShippingEmail(Order order)
    {
        var safeName = System.Net.WebUtility.HtmlEncode(order.ContactName);
        var safeCity = System.Net.WebUtility.HtmlEncode(order.ShippingCity);
        var safeAddress = System.Net.WebUtility.HtmlEncode(order.ShippingAddressLine1);

        var itemsHtml = string.Join("", order.Items.Select(i =>
        {
            var name = System.Net.WebUtility.HtmlEncode(i.ProductName);
            var variant = i.VariantName != null ? $" ({System.Net.WebUtility.HtmlEncode(i.VariantName)})" : "";
            return $"<tr><td style=\"padding:6px 0;font-size:14px;\">{name}{variant}</td>" +
                   $"<td style=\"padding:6px 0;text-align:center;font-size:14px;\">{i.Quantity}</td></tr>";
        }));

        var trackingSection = "";
        if (!string.IsNullOrWhiteSpace(order.TrackingNumber))
        {
            var safeTrackingNumber = System.Net.WebUtility.HtmlEncode(order.TrackingNumber);

            trackingSection = !string.IsNullOrWhiteSpace(order.TrackingUrl)
                ? $"""
                    <div style="background:#f0f7f0;border-radius:8px;padding:16px;margin:16px 0;">
                        <p style="margin:0 0 4px;font-size:14px;font-weight:600;">ტრეკინგ ნომერი</p>
                        <p style="margin:0;font-size:15px;">
                            <a href="{System.Net.WebUtility.HtmlEncode(order.TrackingUrl)}" style="color:#2563eb;text-decoration:underline;">{safeTrackingNumber}</a>
                        </p>
                    </div>
                    """
                : $"""
                    <div style="background:#f0f7f0;border-radius:8px;padding:16px;margin:16px 0;">
                        <p style="margin:0 0 4px;font-size:14px;font-weight:600;">ტრეკინგ ნომერი</p>
                        <p style="margin:0;font-size:15px;">{safeTrackingNumber}</p>
                    </div>
                    """;
        }

        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#333;">
                <h2 style="margin-bottom:4px;">თქვენი შეკვეთა გაიგზავნა! 📦</h2>
                <p style="color:#888;margin-top:0;">შეკვეთის ნომერი: <strong>#{order.Id}</strong></p>

                {trackingSection}

                <table style="width:100%;border-collapse:collapse;margin:16px 0;">
                    <thead>
                        <tr style="border-bottom:2px solid #eee;text-align:left;font-size:13px;color:#888;">
                            <th style="padding:8px 0;">პროდუქტი</th>
                            <th style="padding:8px 0;text-align:center;">რ-ბა</th>
                        </tr>
                    </thead>
                    <tbody>{itemsHtml}</tbody>
                </table>

                <div style="background:#f9f9f9;border-radius:8px;padding:16px;margin:16px 0;">
                    <p style="margin:0 0 4px;font-size:14px;font-weight:600;">მიტანის მისამართი</p>
                    <p style="margin:0;font-size:13px;color:#666;">{safeName}<br/>{safeAddress}<br/>{safeCity}</p>
                </div>

                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;margin:0;">გმადლობთ, რომ აირჩიეთ DressField!</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = order.ContactEmail,
            Subject = $"შეკვეთა #{order.Id} გაიგზავნა — DressField",
            HtmlBody = html,
        });
    }
    private static string? NormalizePromoCode(string? promoCode)
    {
        var normalized = promoCode?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private string BuildBogRedirectUrl(string bogOrderId)
    {
        var trimmedBaseUrl = _paymentPageBaseUrl.Trim();
        var escapedOrderId = Uri.EscapeDataString(bogOrderId);

        if (trimmedBaseUrl.Contains('?', StringComparison.Ordinal))
        {
            var separator = trimmedBaseUrl.EndsWith('?') || trimmedBaseUrl.EndsWith('&') ? "" : "&";
            return $"{trimmedBaseUrl}{separator}order_id={escapedOrderId}";
        }

        return $"{trimmedBaseUrl.TrimEnd('/')}/?order_id={escapedOrderId}";
    }

    internal static string BuildMismatchReason(
        decimal expectedAmount,
        PaymentVerificationResult result,
        bool currencyOk,
        bool amountOk)
    {
        var parts = new List<string>(2);
        if (!currencyOk)
            parts.Add($"Currency mismatch: expected GEL, BOG reported {result.Currency ?? "<missing>"}");
        if (!amountOk)
        {
            parts.Add(result.VerifiedAmount.HasValue
                ? $"Amount mismatch: expected ₾{expectedAmount:F2}, BOG reported ₾{result.VerifiedAmount.Value:F2}"
                : $"Amount missing from BOG receipt (expected ₾{expectedAmount:F2})");
        }
        return string.Join("; ", parts);
    }
}
