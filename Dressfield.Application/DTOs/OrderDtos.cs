using Dressfield.Core.Enums;

namespace Dressfield.Application.DTOs;

// ── Response DTOs ─────────────────────────────────────────────────────────────

public record OrderItemDto(
    int Id,
    int? ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    string? VariantName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public record OrderSummaryDto(
    int Id,
    string? UserId,
    string ContactName,
    string ContactEmail,
    string ContactPhone,
    string ShippingCity,
    OrderStatus Status,
    decimal TotalAmount,
    int ItemCount,
    DateTime CreatedAt);

public record OrderStatusLookupDto(
    int OrderId,
    OrderStatus Status,
    DateTime UpdatedAt);

public record OrderDetailDto(
    int Id,
    string? UserId,
    string ContactName,
    string ContactEmail,
    string ContactPhone,
    string ShippingCity,
    string ShippingAddressLine1,
    string? ShippingAddressLine2,
    string? ShippingPostalCode,
    OrderStatus Status,
    decimal Subtotal,
    decimal ShippingCost,
    decimal TotalAmount,
    string? CustomerNotes,
    string? AdminNotes,
    string? BogOrderId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<OrderItemDto> Items);

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CartItemRequest(
    int ProductId,
    int? VariantId,
    int Quantity);

public record CreateOrderRequest(
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    string ShippingCity,
    string ShippingAddressLine1,
    string? ShippingAddressLine2,
    string? ShippingPostalCode,
    string? CustomerNotes,
    IReadOnlyCollection<CartItemRequest> Items);

public record UpdateOrderStatusRequest(
    OrderStatus Status,
    string? AdminNotes,
    string? ChangedByUserId = null);

// ── Checkout Response ─────────────────────────────────────────────────────────

public record CheckoutResponse(
    int OrderId,
    string? PaymentRedirectUrl,   // null if payment service unavailable
    bool PaymentAvailable);
