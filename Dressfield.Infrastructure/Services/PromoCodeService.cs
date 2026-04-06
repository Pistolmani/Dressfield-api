using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dressfield.Infrastructure.Services;

public class PromoCodeService : IPromoCodeService
{
    private readonly DressfieldDbContext _db;

    public PromoCodeService(DressfieldDbContext db)
    {
        _db = db;
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
        if (request.Subtotal <= 0)
        {
            return new PromoCodeValidationResultDto(
                false,
                "კალათა ცარიელია.",
                null,
                0,
                0);
        }

        var normalizedCode = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return new PromoCodeValidationResultDto(
                false,
                "პრომო კოდი ცარიელია.",
                null,
                0,
                0);
        }

        var promo = await _db.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == normalizedCode);

        if (promo is null || !promo.IsActive)
        {
            return new PromoCodeValidationResultDto(
                false,
                "პრომო კოდი არასწორია ან გამორთულია.",
                null,
                0,
                0);
        }

        if (promo.ExpiresAtUtc.HasValue && promo.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return new PromoCodeValidationResultDto(
                false,
                "პრომო კოდის ვადა გასულია.",
                null,
                0,
                0);
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
