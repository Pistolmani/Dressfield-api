namespace Dressfield.Application.DTOs;

public record CartDto(IReadOnlyCollection<CartItemDto> Items);

public record CartItemDto(
    int ProductId,
    int? VariantId,
    string ProductName,
    string? VariantLabel,
    decimal Price,
    int Quantity,
    string? ImageUrl);

public record SyncCartRequest(IReadOnlyCollection<SyncCartItemRequest> Items);

public record SyncCartItemRequest(
    int ProductId,
    int? VariantId,
    int Quantity);
