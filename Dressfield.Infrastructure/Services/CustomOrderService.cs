using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Core.Enums;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public class CustomOrderService : ICustomOrderService
{
    private readonly DressfieldDbContext _db;
    private readonly IPaymentService _payment;
    private readonly IStorageService _storage;
    private readonly ILogger<CustomOrderService> _logger;

    private static readonly IReadOnlyDictionary<string, decimal> EmbroiderySizeExtraPrices =
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
        ILogger<CustomOrderService> logger)
    {
        _db = db;
        _payment = payment;
        _storage = storage;
        _logger = logger;
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

    private static decimal CalculateEmbroideryExtra(IReadOnlyCollection<CreateCustomOrderDesignRequest> designs) =>
        designs
            .Select(d => ResolveEmbroideryExtra(d.Size))
            .DefaultIfEmpty(0m)
            .Max();

    private static decimal ResolveEmbroideryExtra(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 0m;

        var normalized = size.Trim().ToUpperInvariant();
        return EmbroiderySizeExtraPrices.TryGetValue(normalized, out var extraPrice)
            ? extraPrice
            : 0m;
    }

}
