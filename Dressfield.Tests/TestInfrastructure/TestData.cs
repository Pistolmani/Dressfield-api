using Dressfield.Core.Entities;
using Dressfield.Core.Enums;
using Dressfield.Infrastructure.Data;

namespace Dressfield.Tests.TestInfrastructure;

public static class TestData
{
    /// <summary>Seeds a product with one variant and returns the variant (for stock assertions).</summary>
    public static ProductVariant SeedVariant(DressfieldDbContext db, int stock = 10)
    {
        var product = new Product
        {
            Name = "Test Hoodie",
            Slug = $"test-hoodie-{Guid.NewGuid():N}",
            Description = "test",
            BasePrice = 50m,
            IsActive = true,
        };
        var variant = new ProductVariant { Name = "Size", Value = "L", StockQuantity = stock, IsActive = true };
        product.Variants.Add(variant);
        db.Products.Add(product);
        db.SaveChanges();
        // Detach so the service under test reads fresh DB state (mirrors production,
        // where the request context never tracked entities seeded elsewhere).
        db.ChangeTracker.Clear();
        return variant;
    }

    public static PromoCode SeedPromo(DressfieldDbContext db, string code = "SAVE10",
        decimal percent = 10m, bool isActive = true, DateTime? expiresAtUtc = null,
        int? maxUses = null, int usedCount = 0)
    {
        var promo = new PromoCode
        {
            Code = code,
            DiscountPercentage = percent,
            IsActive = isActive,
            ExpiresAtUtc = expiresAtUtc,
            MaxUses = maxUses,
            UsedCount = usedCount,
        };
        db.PromoCodes.Add(promo);
        db.SaveChanges();
        // Detach so the service under test reads fresh DB state (mirrors production,
        // where the request context never tracked entities seeded elsewhere).
        db.ChangeTracker.Clear();
        return promo;
    }

    public static Order SeedOrder(DressfieldDbContext db, OrderStatus status,
        string? bogOrderId = "bog-1", decimal total = 65m,
        ProductVariant? variant = null, int quantity = 1, string? promoCode = null,
        DateTime? updatedAt = null, DateTime? createdAt = null, string? bogOrderKey = "key-1")
    {
        var order = new Order
        {
            ContactName = "Test User",
            ContactPhone = "555123456",
            ContactEmail = "test@test.local",
            ShippingCity = "Tbilisi",
            ShippingAddressLine1 = "Test St 1",
            Status = status,
            Subtotal = total,
            TotalAmount = total,
            BogOrderId = bogOrderId,
            BogOrderKey = bogOrderKey,
            PromoCode = promoCode,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow,
        };
        order.Items.Add(new OrderItem
        {
            ProductId = variant?.ProductId,
            VariantId = variant?.Id,
            ProductName = "Test Hoodie",
            ProductSlug = "test-hoodie",
            UnitPrice = total,
            Quantity = quantity,
            LineTotal = total * quantity,
        });
        db.Orders.Add(order);
        db.SaveChanges();
        // Detach so the service under test reads fresh DB state (mirrors production,
        // where the request context never tracked entities seeded elsewhere).
        db.ChangeTracker.Clear();
        return order;
    }

    public static CustomOrder SeedCustomOrder(DressfieldDbContext db, CustomOrderStatus status,
        string? bogOrderId = "bog-c1", decimal total = 65m,
        DateTime? updatedAt = null, DateTime? createdAt = null, string? bogOrderKey = "c-key-1")
    {
        var order = new CustomOrder
        {
            ContactName = "Test User",
            ContactPhone = "555123456",
            ContactEmail = "custom@test.local",
            Status = status,
            TotalPrice = total,
            BogOrderId = bogOrderId,
            BogOrderKey = bogOrderKey,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow,
        };
        order.Designs.Add(new CustomOrderDesign { DesignImageUrl = "https://storage.test/designs/d1.webp" });
        db.CustomOrders.Add(order);
        db.SaveChanges();
        // Detach so the service under test reads fresh DB state (mirrors production,
        // where the request context never tracked entities seeded elsewhere).
        db.ChangeTracker.Clear();
        return order;
    }
}
