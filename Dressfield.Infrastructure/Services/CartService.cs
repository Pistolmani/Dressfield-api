using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dressfield.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly DressfieldDbContext _db;

    public CartService(DressfieldDbContext db)
    {
        _db = db;
    }

    public async Task<CartDto> GetCartAsync(string userId)
    {
        var cart = await _db.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart is null || cart.Items.Count == 0)
            return new CartDto(Array.Empty<CartItemDto>());

        var productIds = cart.Items
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        var productsById = products.ToDictionary(p => p.Id);
        var mappedItems = new List<CartItemDto>(cart.Items.Count);

        foreach (var item in cart.Items.OrderByDescending(i => i.AddedAt))
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
                continue;

            ProductVariant? variant = null;
            if (item.VariantId > 0)
            {
                variant = product.Variants.FirstOrDefault(v => v.Id == item.VariantId && v.IsActive);
                if (variant is null)
                    continue;
            }

            var discountedBasePrice = CalculateDiscountedBasePrice(product.BasePrice, product.SalePercentage);
            var price = discountedBasePrice + (variant?.PriceAdjustment ?? 0m);
            var variantLabel = variant is null ? null : $"{variant.Name}: {variant.Value}";
            var imageUrl = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.ImageUrl;

            mappedItems.Add(new CartItemDto(
                item.ProductId,
                item.VariantId == 0 ? null : item.VariantId,
                product.Name,
                variantLabel,
                price,
                item.Quantity,
                imageUrl));
        }

        return new CartDto(mappedItems);
    }

    public async Task<CartDto> SyncCartAsync(string userId, SyncCartRequest request)
    {
        var normalizedItems = request.Items
            .GroupBy(i => $"{i.ProductId}-{i.VariantId ?? 0}")
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    ProductId = first.ProductId,
                    VariantId = first.VariantId ?? 0,
                    Quantity = group.Max(x => x.Quantity),
                };
            })
            .ToList();

        var productIds = normalizedItems
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        var products = productIds.Count == 0
            ? new List<Product>()
            : await _db.Products
                .Include(p => p.Variants)
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToListAsync();

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("ერთ-ერთი პროდუქტი მიუწვდომელია.");

        foreach (var item in normalizedItems)
        {
            if (item.VariantId == 0) continue;

            var product = products.First(p => p.Id == item.ProductId);
            var variantExists = product.Variants.Any(v => v.Id == item.VariantId && v.IsActive);
            if (!variantExists)
                throw new InvalidOperationException("ერთ-ერთი ვარიანტი მიუწვდომელია.");
        }

        var cart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart is null)
        {
            cart = new Cart { UserId = userId };
            _db.Carts.Add(cart);
        }

        if (cart.Items.Count > 0)
            _db.CartItems.RemoveRange(cart.Items);

        cart.Items = normalizedItems
            .Select(i => new CartItem
            {
                ProductId = i.ProductId,
                VariantId = i.VariantId,
                Quantity = i.Quantity,
                AddedAt = DateTime.UtcNow,
            })
            .ToList();
        cart.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task ClearCartAsync(string userId)
    {
        var cart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart is null)
            return;

        if (cart.Items.Count > 0)
            _db.CartItems.RemoveRange(cart.Items);

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static decimal NormalizePercent(decimal value) =>
        Math.Clamp(value, 0m, 100m);

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal CalculateDiscountedBasePrice(decimal basePrice, decimal salePercentage)
    {
        var percent = NormalizePercent(salePercentage);
        return RoundMoney(basePrice * (1m - percent / 100m));
    }
}
