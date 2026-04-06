namespace Dressfield.Application.DTOs;

public record PromoCodeDto(
    int Id,
    string Code,
    decimal DiscountPercentage,
    bool IsActive,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreatePromoCodeRequest(
    string Code,
    decimal DiscountPercentage,
    bool IsActive,
    DateTime? ExpiresAtUtc);

public record UpdatePromoCodeRequest(
    string Code,
    decimal DiscountPercentage,
    bool IsActive,
    DateTime? ExpiresAtUtc);

public record ValidatePromoCodeRequest(
    string Code,
    decimal Subtotal);

public record PromoCodeValidationResultDto(
    bool IsValid,
    string? Message,
    string? Code,
    decimal DiscountPercentage,
    decimal DiscountAmount);

