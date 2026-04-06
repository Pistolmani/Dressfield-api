namespace Dressfield.Application.DTOs;

public record ProductImageDto(int Id, string ImageUrl, string? AltText, int SortOrder, bool IsPrimary);
public record ProductVariantDto(int Id, string Name, string? Value, string? Sku, decimal PriceAdjustment, int StockQuantity, bool IsActive);

public record ProductSummaryDto(
    int Id,
    string Name,
    string Slug,
    string? ShortDescription,
    decimal BasePrice,
    decimal SalePercentage,
    decimal EffectivePrice,
    bool IsOnSale,
    string? PrimaryImageUrl,
    bool IsActive,
    bool IsFeatured);

public record ProductDetailDto(
    int Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string Description,
    decimal BasePrice,
    decimal SalePercentage,
    decimal EffectivePrice,
    bool IsOnSale,
    string? Sku,
    bool IsActive,
    bool IsFeatured,
    IReadOnlyCollection<ProductImageDto> Images,
    IReadOnlyCollection<ProductVariantDto> Variants);

public record CreateProductImageRequest(string ImageUrl, string? AltText, int SortOrder, bool IsPrimary);
public record CreateProductVariantRequest(string Name, string? Value, string? Sku, decimal PriceAdjustment, int StockQuantity, bool IsActive);
public record CreateProductRequest(
    string Name,
    string Slug,
    string? ShortDescription,
    string Description,
    decimal BasePrice,
    decimal SalePercentage,
    string? Sku,
    bool IsActive,
    bool IsFeatured,
    IReadOnlyCollection<CreateProductImageRequest>? Images,
    IReadOnlyCollection<CreateProductVariantRequest>? Variants);
public record UpdateProductRequest(
    string Name,
    string Slug,
    string? ShortDescription,
    string Description,
    decimal BasePrice,
    decimal SalePercentage,
    string? Sku,
    bool IsActive,
    bool IsFeatured,
    IReadOnlyCollection<CreateProductImageRequest>? Images,
    IReadOnlyCollection<CreateProductVariantRequest>? Variants);
