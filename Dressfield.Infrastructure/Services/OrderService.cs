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
    private readonly ILogger<OrderService> _logger;
    private readonly decimal _shippingCost;

    public OrderService(DressfieldDbContext db, IPaymentService payment, IConfiguration configuration, ILogger<OrderService> logger)
    {
        _db = db;
        _payment = payment;
        _logger = logger;
        _shippingCost = decimal.TryParse(configuration["Orders:ShippingCost"], out var sc) ? sc : 5m;
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

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

    public async Task UpdateStatusAsync(int id, UpdateOrderStatusRequest request)
    {
        var order = await _db.Orders.FindAsync(id)
            ?? throw new KeyNotFoundException("შეკვეთა ვერ მოიძებნა");

        var previousStatus = order.Status;
        order.Status = request.Status;
        order.AdminNotes = request.AdminNotes?.Trim();
        order.UpdatedAt = DateTime.UtcNow;

        // Audit log
        _db.OrderStatusLogs.Add(new OrderStatusLog
        {
            OrderId = order.Id,
            FromStatus = previousStatus,
            ToStatus = request.Status,
            ChangedByUserId = request.ChangedByUserId,
            Notes = request.AdminNotes?.Trim(),
        });

        // Queue shipping notification email via outbox
        if (request.Status == OrderStatus.Shipped && !string.IsNullOrEmpty(order.ContactEmail))
            QueueShippingEmail(order.ContactEmail, order.Id);

        await _db.SaveChangesAsync();
    }

    // ── Customer ──────────────────────────────────────────────────────────────

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
                o.UpdatedAt))
            .FirstOrDefaultAsync();
    }

    // ── Checkout ──────────────────────────────────────────────────────────────

    public async Task<CheckoutResponse> CreateAsync(CreateOrderRequest request, string? userId)
    {
        // 1. Load and validate products
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("ერთ-ერთი პროდუქტი მიუწვდომელია.");

        // 2. Build order items with price snapshots
        var items = new List<OrderItem>();
        decimal subtotal = 0;

        foreach (var cartItem in request.Items)
        {
            var product = products.First(p => p.Id == cartItem.ProductId);
            var variant = cartItem.VariantId.HasValue
                ? product.Variants.FirstOrDefault(v => v.Id == cartItem.VariantId.Value && v.IsActive)
                : null;

            var unitPrice = product.BasePrice + (variant?.PriceAdjustment ?? 0);
            var lineTotal = unitPrice * cartItem.Quantity;
            subtotal += lineTotal;

            items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductSlug = product.Slug,
                ProductImageUrl = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.ImageUrl,
                VariantName = variant != null ? $"{variant.Name}: {variant.Value}" : null,
                UnitPrice = unitPrice,
                Quantity = cartItem.Quantity,
                LineTotal = lineTotal
            });
        }

        var orderKey = Guid.NewGuid().ToString("N");

        var order = new Order
        {
            UserId = userId,
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            ShippingCity = request.ShippingCity.Trim(),
            ShippingAddressLine1 = request.ShippingAddressLine1.Trim(),
            ShippingAddressLine2 = request.ShippingAddressLine2?.Trim(),
            ShippingPostalCode = request.ShippingPostalCode?.Trim(),
            CustomerNotes = request.CustomerNotes?.Trim(),
            Subtotal = subtotal,
            ShippingCost = _shippingCost,
            TotalAmount = subtotal + _shippingCost,
            BogOrderKey = orderKey,
            Status = OrderStatus.Pending,
            Items = items
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // 3. Create BOG payment session
        var description = $"DressField შეკვეთა #{order.Id}";
        var paymentResult = await _payment.CreateSessionAsync(order.Id, order.TotalAmount, orderKey, description);

        if (paymentResult.Success && paymentResult.BogOrderId != null)
        {
            order.BogOrderId = paymentResult.BogOrderId;
            order.Status = OrderStatus.AwaitingPayment;
            await _db.SaveChangesAsync();
        }

        return new CheckoutResponse(
            order.Id,
            paymentResult.RedirectUrl,
            paymentResult.Success);
    }

    // ── Payment Callback ──────────────────────────────────────────────────────

    public async Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.BogOrderId == bogOrderId);

        if (order == null)
        {
            _logger.LogWarning("Payment callback for unknown BOG order {BogOrderId}", bogOrderId);
            return;
        }

        // Reject callbacks without the per-order secret, or with a mismatched secret.
        if (string.IsNullOrWhiteSpace(orderKey) || order.BogOrderKey != orderKey)
        {
            _logger.LogWarning("Payment callback key validation failed for order {OrderId}", order.Id);
            return;
        }

        // Idempotency guard — only process if still awaiting payment
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            _logger.LogInformation("Duplicate callback for order {OrderId} (status: {Status}) — skipping",
                order.Id, order.Status);
            return;
        }

        var result = await _payment.VerifyCallbackAsync(bogOrderId);

        var previousStatus = order.Status;
        order.Status    = result.IsApproved ? OrderStatus.Paid : OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        // Audit log
        _db.OrderStatusLogs.Add(new OrderStatusLog
        {
            OrderId = order.Id,
            FromStatus = previousStatus,
            ToStatus = order.Status,
            ChangedByUserId = null, // system event
            Notes = $"BOG callback: {(result.IsApproved ? "approved" : "declined")} (txn: {result.TransactionId})",
        });

        // Queue confirmation email via outbox
        if (result.IsApproved && !string.IsNullOrEmpty(order.ContactEmail))
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

        await _db.SaveChangesAsync();

        _logger.LogInformation("Order {OrderId} payment {Result} (BOG: {BogOrderId})",
            order.Id, result.IsApproved ? "approved" : "declined", bogOrderId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
            order.ShippingCost,
            order.TotalAmount,
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

    // ── Email outbox helpers ───────────────────────────────────────────────────
    // Emails are inserted into PendingEmails and delivered by EmailOutboxWorker.
    // This decouples order processing from SMTP availability.

    private void QueueConfirmationEmail(string to, int orderId, string itemsHtml, string total)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                <h2 style="margin-bottom:4px;">შეკვეთა წარმატებით გაფორმდა!</h2>
                <p style="color:#888;margin-top:0;">შეკვეთის ნომერი: <strong>#{orderId}</strong></p>
                <table style="width:100%;border-collapse:collapse;margin:16px 0;">
                    <thead>
                        <tr style="border-bottom:1px solid #eee;text-align:left;font-size:13px;color:#888;">
                            <th style="padding:8px 0;">პროდუქტი</th>
                            <th style="padding:8px 0;">რ-ბა</th>
                            <th style="padding:8px 0;text-align:right;">ფასი</th>
                        </tr>
                    </thead>
                    <tbody>{itemsHtml}</tbody>
                </table>
                <p style="font-size:16px;font-weight:600;text-align:right;">სულ: {total}</p>
                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;">გმადლობთ შეკვეთისთვის! ჩვენი გუნდი დაგიკავშირდებათ მალე.</p>
                <p style="color:#888;font-size:13px;">— DressField</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = $"შეკვეთა #{orderId} — DressField",
            HtmlBody = html,
        });
    }

    private void QueueShippingEmail(string to, int orderId)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                <h2>თქვენი შეკვეთა გაიგზავნა!</h2>
                <p>შეკვეთის ნომერი: <strong>#{orderId}</strong></p>
                <p>თქვენი შეკვეთა გაგზავნილია და მალე მიიღებთ.</p>
                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;">— DressField</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = $"შეკვეთა #{orderId} გაიგზავნა — DressField",
            HtmlBody = html,
        });
    }
}
