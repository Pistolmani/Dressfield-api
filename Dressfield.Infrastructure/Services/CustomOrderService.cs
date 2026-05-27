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

public class CustomOrderService : ICustomOrderService
{
    private readonly DressfieldDbContext _db;
    private readonly IPaymentService _payment;
    private readonly IStorageService _storage;
    private readonly ILogger<CustomOrderService> _logger;
    private readonly IReadOnlyDictionary<string, decimal> _embroiderySizeExtraPrices;

    /// <summary>Default embroidery prices, used when <c>CustomOrders:EmbroiderySizePrices</c> is not configured.</summary>
    private static readonly IReadOnlyDictionary<string, decimal> DefaultEmbroiderySizeExtraPrices =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = 0m,
            ["M"] = 10m,
            ["L"] = 20m,
            ["XL"] = 35m
        };

    public CustomOrderService(
        DressfieldDbContext db,
        IPaymentService payment,
        IStorageService storage,
        ILogger<CustomOrderService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _payment = payment;
        _storage = storage;
        _logger = logger;

        var configuredPrices = configuration.GetSection("CustomOrders:EmbroiderySizePrices")
            .Get<Dictionary<string, decimal>>();
        _embroiderySizeExtraPrices = configuredPrices is { Count: > 0 }
            ? new Dictionary<string, decimal>(configuredPrices, StringComparer.OrdinalIgnoreCase)
            : DefaultEmbroiderySizeExtraPrices;
    }

    public async Task<IReadOnlyCollection<CustomOrderSummaryDto>> GetAdminAsync(CustomOrderStatus? status)
    {
        var query = _db.CustomOrders
            .AsNoTracking()
            .Include(o => o.BaseProduct)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => MapToSummary(o))
            .ToListAsync();
    }

    public async Task<CustomOrderDetailDto?> GetAdminByIdAsync(int id)
    {
        var order = await _db.CustomOrders
            .AsNoTracking()
            .Include(o => o.BaseProduct)
            .Include(o => o.Designs)
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? null : MapDetail(order);
    }

    public async Task UpdateStatusAsync(int id, UpdateCustomOrderStatusRequest request)
    {
        var order = await _db.CustomOrders.FindAsync(id)
            ?? throw new KeyNotFoundException("შეკვეთა ვერ მოიძებნა");

        var previousStatus = order.Status;
        order.Status = request.Status;
        order.AdminNotes = request.AdminNotes?.Trim();
        order.UpdatedAt = DateTime.UtcNow;

        _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
        {
            CustomOrderId = order.Id,
            FromStatus = previousStatus,
            ToStatus = request.Status,
            Notes = request.AdminNotes?.Trim(),
        });

        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyCollection<CustomOrderSummaryDto>> GetByUserAsync(string userId)
    {
        return await _db.CustomOrders
            .AsNoTracking()
            .Include(o => o.BaseProduct)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => MapToSummary(o))
            .ToListAsync();
    }

    public async Task<CustomOrderDetailDto?> GetByIdForUserAsync(int id, string userId)
    {
        var order = await _db.CustomOrders
            .AsNoTracking()
            .Include(o => o.BaseProduct)
            .Include(o => o.Designs)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        return order is null ? null : MapDetail(order);
    }

    public async Task<CustomOrderCheckoutResponse> CreateAsync(CreateCustomOrderRequest request, string? userId)
    {
        decimal baseProductPrice = 0m;

        if (request.BaseProductId.HasValue)
        {
            var baseProduct = await _db.Products
                .AsNoTracking()
                .Where(p => p.Id == request.BaseProductId.Value && p.IsActive)
                .Select(p => new { p.BasePrice })
                .FirstOrDefaultAsync();
            if (baseProduct is null)
                throw new KeyNotFoundException("არჩეული პროდუქტი ვერ მოიძებნა");

            baseProductPrice = baseProduct.BasePrice;
        }

        var calculatedTotalPrice = baseProductPrice + CalculateEmbroideryExtra(request.Designs);

        // Custom order keys are prefixed with "c-" so the payment callback can distinguish them
        var orderKey = "c-" + Guid.NewGuid().ToString("N");

        var order = new CustomOrder
        {
            UserId = userId,
            BaseProductId = request.BaseProductId,
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            TotalPrice = calculatedTotalPrice,
            CustomerNotes = request.CustomerNotes?.Trim(),
            BogOrderKey = orderKey,
            Status = CustomOrderStatus.Pending,
            Designs = request.Designs.Select(d => new CustomOrderDesign
            {
                DesignImageUrl = d.DesignImageUrl.Trim(),
                Placement = d.Placement?.Trim(),
                Size = d.Size?.Trim(),
                ThreadColor = d.ThreadColor?.Trim(),
                Width = d.Width,
                Height = d.Height,
                PositionX = d.PositionX,
                PositionY = d.PositionY,
                SortOrder = d.SortOrder
            }).ToList()
        };

        _db.CustomOrders.Add(order);
        await _db.SaveChangesAsync();

        var description = $"DressField Custom Order #{order.Id}";
        var paymentResult = await _payment.CreateSessionAsync(order.Id, order.TotalPrice, orderKey, description);

        if (paymentResult.Success && paymentResult.BogOrderId != null)
        {
            order.BogOrderId = paymentResult.BogOrderId;
            order.Status = CustomOrderStatus.AwaitingPayment;
            order.UpdatedAt = DateTime.UtcNow;

            _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = order.Id,
                FromStatus = CustomOrderStatus.Pending,
                ToStatus = CustomOrderStatus.AwaitingPayment,
                Notes = "BOG payment session created",
            });
            await _db.SaveChangesAsync();
        }
        else
        {
            order.Status = CustomOrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = order.Id,
                FromStatus = CustomOrderStatus.Pending,
                ToStatus = CustomOrderStatus.Cancelled,
                Notes = $"BOG session creation failed: {paymentResult.ErrorMessage}",
            });
            await _db.SaveChangesAsync();
        }

        return new CustomOrderCheckoutResponse(order.Id, paymentResult.RedirectUrl, paymentResult.Success);
    }

    public async Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey)
    {
        // Atomic claim: only one concurrent caller transitions AwaitingPayment → PaymentProcessing.
        // RSA signature on the controller already authenticates the caller; orderKey is not re-checked here.
        var claimed = await _db.CustomOrders
            .Where(o => o.BogOrderId == bogOrderId && o.Status == CustomOrderStatus.AwaitingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, CustomOrderStatus.PaymentProcessing)
                                      .SetProperty(o => o.UpdatedAt, DateTime.UtcNow));

        if (claimed == 0)
        {
            _logger.LogInformation("Callback for {BogOrderId} not claimable (already handled or unknown)", bogOrderId);
            return;
        }

        var order = await _db.CustomOrders.FirstAsync(o => o.BogOrderId == bogOrderId);

        var result = await _payment.VerifyCallbackAsync(bogOrderId);

        var currencyOk = string.Equals(result.Currency, "GEL", StringComparison.OrdinalIgnoreCase);
        var amountOk = result.VerifiedAmount.HasValue
                       && Math.Abs(result.VerifiedAmount.Value - order.TotalPrice) <= 0.01m;

        if (result.IsApproved && (!currencyOk || !amountOk))
        {
            var reason = OrderService.BuildMismatchReason(order.TotalPrice, result, currencyOk, amountOk);

            _logger.LogCritical(
                "BOG payment verification failed for custom order {OrderId} — {Reason} (BOG: {BogOrderId})",
                order.Id, reason, bogOrderId);

            order.Status = CustomOrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = order.Id,
                FromStatus = CustomOrderStatus.PaymentProcessing,
                ToStatus = CustomOrderStatus.Cancelled,
                Notes = reason,
            });

            await _db.SaveChangesAsync();
            return;
        }

        if (BogPaymentStatus.IsPending(result.Status))
        {
            order.Status = CustomOrderStatus.AwaitingPayment;
            order.UpdatedAt = DateTime.UtcNow;

            _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = order.Id,
                FromStatus = CustomOrderStatus.PaymentProcessing,
                ToStatus = CustomOrderStatus.AwaitingPayment,
                Notes = $"BOG callback not terminal yet: {result.Status}",
            });

            await _db.SaveChangesAsync();
            return;
        }

        order.Status = result.IsApproved ? CustomOrderStatus.Reviewing : CustomOrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        _db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
        {
            CustomOrderId = order.Id,
            FromStatus = CustomOrderStatus.PaymentProcessing,
            ToStatus = order.Status,
            Notes = $"BOG callback: {(result.IsApproved ? "approved" : "declined")} (txn: {result.TransactionId})",
        });

        if (result.IsApproved && !string.IsNullOrEmpty(order.ContactEmail))
            QueueConfirmationEmail(order.ContactEmail, order.Id, order.TotalPrice);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Custom order {OrderId} payment {Result} (BOG: {BogOrderId})",
            order.Id, result.IsApproved ? "approved" : "declined", bogOrderId);
    }

    private CustomOrderDetailDto MapDetail(CustomOrder o) =>
        new(
            o.Id,
            o.UserId,
            o.BaseProductId,
            o.BaseProduct?.Name,
            o.ContactName,
            o.ContactPhone,
            o.ContactEmail,
            o.Status,
            o.TotalPrice,
            o.CustomerNotes,
            o.AdminNotes,
            o.CreatedAt,
            o.UpdatedAt,
            o.Designs
                .OrderBy(d => d.SortOrder)
                .Select(d => new CustomOrderDesignDto(
                    d.Id,
                    _storage.GetSignedReadUrl(d.DesignImageUrl),
                    d.Placement,
                    d.Size,
                    d.ThreadColor,
                    d.Width,
                    d.Height,
                    d.PositionX,
                    d.PositionY,
                    d.SortOrder))
                .ToList());

    private static CustomOrderSummaryDto MapToSummary(CustomOrder o) =>
        new(
            o.Id,
            o.UserId,
            o.BaseProductId,
            o.BaseProduct?.Name,
            o.ContactName,
            o.ContactPhone,
            o.ContactEmail,
            o.Status,
            o.TotalPrice,
            o.CreatedAt);

    private decimal CalculateEmbroideryExtra(IReadOnlyCollection<CreateCustomOrderDesignRequest> designs) =>
        designs
            .Select(d => ResolveEmbroideryExtra(d.Size))
            .DefaultIfEmpty(0m)
            .Max();

    private decimal ResolveEmbroideryExtra(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 0m;

        var normalized = size.Trim().ToUpperInvariant();
        return _embroiderySizeExtraPrices.TryGetValue(normalized, out var extraPrice)
            ? extraPrice
            : 0m;
    }

    /// <summary>
    /// Persists the BOG session details for a custom order after successful session creation,
    /// with retries to guard against transient DB failures.
    /// Mirrors <see cref="OrderService.SaveBogSessionWithRetryAsync"/>.
    /// </summary>
    private async Task SaveBogSessionWithRetryAsync(
        int orderId, string bogOrderId, string bogOrderKey, decimal totalPrice, string? redirectUrl)
    {
        int[] retryDelaysMs = [200, 1000, 5000];

        for (var attempt = 0; attempt < retryDelaysMs.Length + 1; attempt++)
        {
            try
            {
                await _db.CustomOrders
                    .Where(o => o.Id == orderId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.BogOrderId, bogOrderId)
                        .SetProperty(o => o.Status, CustomOrderStatus.AwaitingPayment)
                        .SetProperty(o => o.UpdatedAt, DateTime.UtcNow));
                return;
            }
            catch (Exception ex) when (attempt < retryDelaysMs.Length)
            {
                _logger.LogWarning(ex,
                    "Transient DB failure saving BOG session for custom order {OrderId} (attempt {Attempt}/{Max}). Retrying in {Delay}ms.",
                    orderId, attempt + 1, retryDelaysMs.Length + 1, retryDelaysMs[attempt]);
                await Task.Delay(retryDelaysMs[attempt]);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex,
                    "CRITICAL: Failed to persist BOG session for custom order {OrderId} after {Max} attempts. " +
                    "BogOrderKey={BogOrderKey} BogOrderId={BogOrderId} Amount=₾{Amount} RedirectUrl={RedirectUrl}. " +
                    "The customer has a valid payment link and may complete payment. " +
                    "Do NOT cancel this order manually. The reaper will reconcile via LookupByExternalOrderIdAsync.",
                    orderId, retryDelaysMs.Length + 1, bogOrderKey, bogOrderId, totalPrice, redirectUrl);
                return;
            }
        }
    }

    private void QueueConfirmationEmail(string to, int orderId, decimal total)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#333;">
                <h2 style="margin-bottom:4px;">გადახდა მიღებულია!</h2>
                <p style="color:#888;margin-top:0;">ინდივიდუალური შეკვეთის ნომერი: <strong>#{orderId}</strong></p>

                <p style="font-size:16px;font-weight:600;text-align:right;margin:16px 0;">სულ: ₾{total:F2}</p>

                <div style="background:#f9f9f9;border-radius:8px;padding:16px;margin:16px 0;">
                    <p style="margin:0 0 4px;font-size:14px;font-weight:600;">რა ხდება შემდეგ?</p>
                    <p style="margin:0;font-size:13px;color:#666;">ჩვენი გუნდი გადახედავს თქვენს დიზაინს. დადასტურების შემდეგ დავიწყებთ ნაქარგობის წარმოებას და გამოგზავნისას მიიღებთ შეტყობინებას.</p>
                </div>

                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;margin:0;">გმადლობთ, რომ აირჩიეთ DressField!</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = $"ინდივიდუალური შეკვეთა #{orderId} მიღებულია — DressField",
            HtmlBody = html,
        });
    }

}
