using Dressfield.Core.Enums;

namespace Dressfield.Application.DTOs;

public record CustomOrderDesignDto(
    int Id,
    string DesignImageUrl,
    string? Placement,
    string? Size,
    string? ThreadColor,
    decimal? Width,
    decimal? Height,
    decimal? PositionX,
    decimal? PositionY,
    int SortOrder);

public record CustomOrderSummaryDto(
    int Id,
    string? UserId,
    int? BaseProductId,
    string? BaseProductName,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    CustomOrderStatus Status,
    decimal TotalPrice,
    DateTime CreatedAt);

public record CustomOrderDetailDto(
    int Id,
    string? UserId,
    int? BaseProductId,
    string? BaseProductName,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    CustomOrderStatus Status,
    decimal TotalPrice,
    string? CustomerNotes,
    string? AdminNotes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<CustomOrderDesignDto> Designs);

public record CreateCustomOrderDesignRequest(
    string DesignImageUrl,
    string? Placement,
    string? Size,
    string? ThreadColor,
    decimal? Width,
    decimal? Height,
    decimal? PositionX,
    decimal? PositionY,
    int SortOrder);

public record CreateCustomOrderRequest(
    int? BaseProductId,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    decimal TotalPrice,
    string? CustomerNotes,
    IReadOnlyCollection<CreateCustomOrderDesignRequest> Designs);

public record UpdateCustomOrderStatusRequest(
    CustomOrderStatus Status,
    string? AdminNotes);

// Returned from POST /api/custom-orders — mirrors CheckoutResponse for regular orders
public record CustomOrderCheckoutResponse(
    int OrderId,
    string? PaymentRedirectUrl,
    bool PaymentSuccess);
