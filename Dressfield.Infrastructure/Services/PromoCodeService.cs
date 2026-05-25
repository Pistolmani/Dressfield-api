using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace Dressfield.Infrastructure.Services;

public class PromoCodeService : IPromoCodeService
{
    private const string OpaqueInvalidMessage = "პრომო კოდი არასწორია ან მიუწვდომელია.";

    private readonly DressfieldDbContext _db;
    private readonly ILogger<PromoCodeService> _logger;

    public PromoCodeService(DressfieldDbContext db, ILogger<PromoCodeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private PromoCodeValidationResultDto InvalidResult(string serverReason, string code)
    {
        // Server-side log keeps the precise reason for diagnostics; clients always get the
        // same opaque response so promo-code state can't be enumerated through this endpoint.
        _logger.LogInformation("Promo code validation rejected ({Reason}) code={Code}", serverReason, code);
        return new PromoCodeValidationResultDto(false, OpaqueInvalidMessage, null, 0, 0);
    }

    public async Task<IReadOnlyCollection<PromoCodeDto>> GetAdminAsync()
    {
        return await _db.PromoCodes
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(MapDtoExpression())
            .ToListAsync();
    }

    public async Task<PromoCodeDto> CreateAsync(CreatePromoCodeRequest request)
    {
        var normalizedCode = NormalizeCode(request.Code);
        var exists = await _db.PromoCodes.AnyAsync(x => x.Code == normalizedCode);
        if (exists)
        {
            throw new InvalidOperationException("ასეთი პრომო კოდი უკვე არსებობს.");
        }

        var now = DateTime.UtcNow;
        var entity = new PromoCode
        {
            Code = normalizedCode,
            DiscountPercentage = request.DiscountPercentage,
            IsActive = request.IsActive,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.PromoCodes.Add(entity);
        await _db.SaveChangesAsync();

        return MapDto(entity);
    }

    public async Task<PromoCodeDto> UpdateAsync(int id, UpdatePromoCodeRequest request)
    {
        var entity = await _db.PromoCodes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("პრომო კოდი ვერ მოიძებნა.");

        var normalizedCode = NormalizeCode(request.Code);
        var exists = await _db.PromoCodes.AnyAsync(x => x.Code == normalizedCode && x.Id != id);
        if (exists)
        {
            throw new InvalidOperationException("ასეთი პრომო კოდი უკვე არსებობს.");
        }

        entity.Code = normalizedCode;
        entity.DiscountPercentage = request.DiscountPercentage;
        entity.IsActive = request.IsActive;
        entity.ExpiresAtUtc = request.ExpiresAtUtc;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapDto(entity);
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _db.PromoCodes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("პრომო კოდი ვერ მოიძებნა.");

        _db.PromoCodes.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<PromoCodeValidationResultDto> ValidateAsync(ValidatePromoCodeRequest request)
    {
        var normalizedCode = NormalizeCode(request.Code);

        if (request.Subtotal <= 0)
            return InvalidResult("empty-cart", normalizedCode);

        if (string.IsNullOrWhiteSpace(normalizedCode))
            return InvalidResult("empty-code", normalizedCode);

        var promo = await _db.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == normalizedCode);

        if (promo is null || !promo.IsActive)
            return InvalidResult("not-found-or-inactive", normalizedCode);

        if (promo.ExpiresAtUtc.HasValue && promo.ExpiresAtUtc.Value <= DateTime.UtcNow)
            return InvalidResult("expired", normalizedCode);

        if (promo.MaxUses.HasValue && promo.UsedCount >= promo.MaxUses.Value)
            return InvalidResult("max-uses-reached", normalizedCode);

        if (promo.MaxUsesPerUser.HasValue && !string.IsNullOrEmpty(request.UserId))
        {
            var userUses = await _db.Orders
                .CountAsync(o => o.UserId == request.UserId
                              && o.PromoCode == promo.Code
                              && o.Status != Core.Enums.OrderStatus.Cancelled);

            if (userUses >= promo.MaxUsesPerUser.Value)
                return InvalidResult("per-user-limit-reached", normalizedCode);
        }

        var discountPercent = ClampPercent(promo.DiscountPercentage);
        var discountAmount = RoundMoney(request.Subtotal * discountPercent / 100m);

        return new PromoCodeValidationResultDto(
            true,
            null,
            promo.Code,
            discountPercent,
            discountAmount);
    }

    private static decimal ClampPercent(decimal value) => Math.Clamp(value, 0m, 100m);

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeCode(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    private static PromoCodeDto MapDto(PromoCode x) =>
        new(
            x.Id,
            x.Code,
            x.DiscountPercentage,
            x.IsActive,
            x.ExpiresAtUtc,
            x.CreatedAt,
            x.UpdatedAt);

    private static Expression<Func<PromoCode, PromoCodeDto>> MapDtoExpression() =>
        x => new PromoCodeDto(
            x.Id,
            x.Code,
            x.DiscountPercentage,
            x.IsActive,
            x.ExpiresAtUtc,
            x.CreatedAt,
            x.UpdatedAt);
}
