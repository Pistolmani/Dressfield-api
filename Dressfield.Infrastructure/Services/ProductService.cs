using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly DressfieldDbContext _db;
    private readonly IStorageService _storage;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        DressfieldDbContext db,
        IStorageService storage,
        ILogger<ProductService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<ProductSummaryDto>> GetActiveAsync(string? search) =>
        await BuildSummaryQuery(false, search).ToListAsync();

    public async Task<IReadOnlyCollection<ProductSummaryDto>> GetAdminAsync(string? search) =>
        await BuildSummaryQuery(true, search).ToListAsync();

    public async Task<ProductDetailDto?> GetActiveByIdAsync(int id) =>
        MapDetail(await BuildDetailEntityQuery(false).FirstOrDefaultAsync(x => x.Id == id));

    public async Task<ProductDetailDto?> GetActiveBySlugAsync(string slug) =>
        MapDetail(await BuildDetailEntityQuery(false).FirstOrDefaultAsync(x => x.Slug == slug));

    public async Task<ProductDetailDto?> GetAdminByIdAsync(int id) =>
        MapDetail(await BuildDetailEntityQuery(true).FirstOrDefaultAsync(x => x.Id == id));

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request)
    {
        await EnsureSlugIsUniqueAsync(request.Slug, null);
        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            ShortDescription = request.ShortDescription?.Trim(),
            Description = request.Description.Trim(),
            BasePrice = request.BasePrice,
            SalePercentage = NormalizeSalePercentage(request.SalePercentage),
            Sku = request.Sku?.Trim(),
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            Images = MapImages(request.Images),
            Variants = MapVariants(request.Variants)
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return (await GetAdminByIdAsync(product.Id))!;
    }

    public async Task<ProductDetailDto> UpdateAsync(int id, UpdateProductRequest request)
    {
        var product = await _db.Products
            .Include(x => x.Images)
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("პროდუქტი ვერ მოიძებნა");

        var previousImageUrls = product.Images
            .Select(x => x.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await EnsureSlugIsUniqueAsync(request.Slug, id);

        product.Name = request.Name.Trim();
        product.Slug = request.Slug.Trim().ToLowerInvariant();
        product.ShortDescription = request.ShortDescription?.Trim();
        product.Description = request.Description.Trim();
        product.BasePrice = request.BasePrice;
        product.SalePercentage = NormalizeSalePercentage(request.SalePercentage);
        product.Sku = request.Sku?.Trim();
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.UpdatedAt = DateTime.UtcNow;

        _db.ProductImages.RemoveRange(product.Images);
        _db.ProductVariants.RemoveRange(product.Variants);
        product.Images = MapImages(request.Images);
        product.Variants = MapVariants(request.Variants);

        await _db.SaveChangesAsync();

        var nextImageUrls = product.Images
            .Select(x => x.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removedUrl in previousImageUrls.Except(nextImageUrls, StringComparer.OrdinalIgnoreCase))
        {
            await DeleteImageBestEffortAsync(removedUrl);
        }

        return (await GetAdminByIdAsync(product.Id))!;
    }

    public async Task DeleteAsync(int id)
    {
        var product = await _db.Products
            .Include(x => x.Images)
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException("პროდუქტი ვერ მოიძებნა");

        var imageUrls = product.Images
            .Select(x => x.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToArray();

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        foreach (var imageUrl in imageUrls)
        {
            await DeleteImageBestEffortAsync(imageUrl);
        }
    }

    private async Task DeleteImageBestEffortAsync(string imageUrl)
    {
        try
        {
            await _storage.DeleteAsync(imageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete orphaned product image from storage: {ImageUrl}", imageUrl);
        }
    }

    private IQueryable<ProductSummaryDto> BuildSummaryQuery(bool includeInactive, string? search)
    {
        var query = _db.Products.AsNoTracking().Include(x => x.Images).AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(normalized) || x.Description.ToLower().Contains(normalized));
        }

        return query
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.Name)
            .Select(x => new ProductSummaryDto(
                x.Id,
                x.Name,
                x.Slug,
                x.ShortDescription,
                x.BasePrice,
                x.SalePercentage,
                x.BasePrice * (1m - ((x.SalePercentage < 0m ? 0m : (x.SalePercentage > 100m ? 100m : x.SalePercentage)) / 100m)),
                x.SalePercentage > 0,
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                x.IsActive,
                x.IsFeatured));
    }

    private IQueryable<Product> BuildDetailEntityQuery(bool includeInactive)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(x => x.Images)
            .Include(x => x.Variants)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return query;
    }

    private static ProductDetailDto? MapDetail(Product? product)
    {
        if (product is null)
        {
            return null;
        }

        return new ProductDetailDto(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.Description,
            product.BasePrice,
            product.SalePercentage,
            CalculateEffectivePrice(product.BasePrice, product.SalePercentage),
            product.SalePercentage > 0,
            product.Sku,
            product.IsActive,
            product.IsFeatured,
            product.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.AltText, i.SortOrder, i.IsPrimary))
                .ToList(),
            product.Variants
                .OrderBy(v => v.Name)
                .Select(v => new ProductVariantDto(v.Id, v.Name, v.Value, v.Sku, v.PriceAdjustment, v.StockQuantity, v.IsActive))
                .ToList());
    }

    private async Task EnsureSlugIsUniqueAsync(string slug, int? currentId)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var exists = await _db.Products.AnyAsync(x => x.Slug == normalized && (!currentId.HasValue || x.Id != currentId.Value));
        if (exists)
        {
            throw new InvalidOperationException("ამ slug-ით პროდუქტი უკვე არსებობს");
        }
    }

    private static List<ProductImage> MapImages(IReadOnlyCollection<CreateProductImageRequest>? requests) =>
        requests?.Select(x => new ProductImage
        {
            ImageUrl = x.ImageUrl.Trim(),
            AltText = x.AltText?.Trim(),
            SortOrder = x.SortOrder,
            IsPrimary = x.IsPrimary
        }).ToList() ?? new List<ProductImage>();

    private static List<ProductVariant> MapVariants(IReadOnlyCollection<CreateProductVariantRequest>? requests) =>
        requests?.Select(x => new ProductVariant
        {
            Name = x.Name.Trim(),
            Value = x.Value?.Trim(),
            Sku = x.Sku?.Trim(),
            PriceAdjustment = x.PriceAdjustment,
            StockQuantity = x.StockQuantity,
            IsActive = x.IsActive
        }).ToList() ?? new List<ProductVariant>();

    private static decimal NormalizeSalePercentage(decimal value) =>
        Math.Clamp(value, 0m, 100m);

    private static decimal CalculateEffectivePrice(decimal basePrice, decimal salePercentage)
    {
        var percent = NormalizeSalePercentage(salePercentage);
        var factor = 1m - (percent / 100m);
        return decimal.Round(basePrice * factor, 2, MidpointRounding.AwayFromZero);
    }
}
