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
    private readonly ILogger<CustomOrderService> _logger;
    private static readonly IReadOnlyDictionary<string, decimal> EmbroiderySizeExtraPrices =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = 0m,
            ["M"] = 10m,
            ["L"] = 20m,
            ["XL"] = 35m
        };

    public CustomOrderService(DressfieldDbContext db, IPaymentService payment, ILogger<CustomOrderService> logger)
    {
        _db = db;
        _payment = payment;
        _logger = logger;
    }

    // ── Admin ────────────────────────────────────────────────────────────────

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

        order.Status = request.Status;
        order.AdminNotes = request.AdminNotes?.Trim();
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    // ── Customer ─────────────────────────────────────────────────────────────

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

    // ── Public ───────────────────────────────────────────────────────────────

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

        // Prefix with "c-" so the payment callback can distinguish custom orders from regular orders
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
            Status = CustomOrderStatus.AwaitingPayment,
            BogOrderKey = orderKey,
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

        // Create BOG payment session
        var description = $"DressField კასტომ შეკვეთა #{order.Id}";
        var paymentResult = await _payment.CreateSessionAsync(order.Id, order.TotalPrice, orderKey, description);

        if (paymentResult.Success && paymentResult.BogOrderId != null)
        {
            order.BogOrderId = paymentResult.BogOrderId;
            await _db.SaveChangesAsync();
        }
        else
        {
            _logger.LogWarning("BOG session creation failed for custom order {OrderId}: {Error}",
                order.Id, paymentResult.ErrorMessage);
        }

        return new CustomOrderCheckoutResponse(order.Id, paymentResult.RedirectUrl, paymentResult.Success);
    }

    public async Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey)
    {
        var order = await _db.CustomOrders
            .FirstOrDefaultAsync(o => o.BogOrderId == bogOrderId);

        if (order is null)
        {
            _logger.LogWarning("Custom order callback: no order found for BogOrderId {BogOrderId}", bogOrderId);
            return;
        }

        // Verify key matches to prevent spoofing
        if (!string.IsNullOrEmpty(orderKey) && order.BogOrderKey != orderKey)
        {
            _logger.LogWarning("Custom order callback key mismatch for order {OrderId}", order.Id);
            return;
        }

        var verification = await _payment.VerifyCallbackAsync(bogOrderId);

        if (verification.IsApproved && order.Status == CustomOrderStatus.AwaitingPayment)
        {
            order.Status = CustomOrderStatus.Pending; // paid → moves to review queue
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Custom order {OrderId} payment confirmed, moved to Pending review", order.Id);
        }
        else
        {
            _logger.LogInformation("Custom order {OrderId} callback status: {Status}", order.Id, verification.Status);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static CustomOrderDetailDto MapDetail(CustomOrder o) =>
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
                    d.DesignImageUrl,
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
