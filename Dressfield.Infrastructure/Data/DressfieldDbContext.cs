using Dressfield.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dressfield.Infrastructure.Data;

public class DressfieldDbContext : IdentityDbContext<ApplicationUser>
{
    public DressfieldDbContext(DbContextOptions<DressfieldDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<CustomOrder> CustomOrders => Set<CustomOrder>();
    public DbSet<CustomOrderDesign> CustomOrderDesigns => Set<CustomOrderDesign>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusLog> OrderStatusLogs => Set<OrderStatusLog>();
    public DbSet<PendingEmail> PendingEmails => Set<PendingEmail>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ShortDescription).HasMaxLength(300);
            entity.Property(e => e.Description).HasMaxLength(5000).IsRequired();
            entity.Property(e => e.Sku).HasMaxLength(64);
            entity.Property(e => e.BasePrice).HasPrecision(18, 2);
            entity.Property(e => e.SalePercentage).HasPrecision(5, 2);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        builder.Entity<ProductImage>(entity =>
        {
            entity.Property(e => e.ImageUrl).HasMaxLength(500).IsRequired();
            entity.Property(e => e.AltText).HasMaxLength(200);
            entity.HasIndex(e => new { e.ProductId, e.SortOrder });
            entity.HasOne(e => e.Product).WithMany(p => p.Images).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductVariant>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(100);
            entity.Property(e => e.Sku).HasMaxLength(64);
            entity.Property(e => e.PriceAdjustment).HasPrecision(18, 2);
            entity.HasOne(e => e.Product).WithMany(p => p.Variants).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CustomOrder>(entity =>
        {
            entity.Property(e => e.ContactName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ContactPhone).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ContactEmail).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TotalPrice).HasPrecision(18, 2);
            entity.Property(e => e.CustomerNotes).HasMaxLength(1000);
            entity.Property(e => e.AdminNotes).HasMaxLength(1000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            entity.HasOne(e => e.BaseProduct).WithMany().HasForeignKey(e => e.BaseProductId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        builder.Entity<CustomOrderDesign>(entity =>
        {
            entity.Property(e => e.DesignImageUrl).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Placement).HasMaxLength(50);
            entity.Property(e => e.Size).HasMaxLength(20);
            entity.Property(e => e.ThreadColor).HasMaxLength(20);
            entity.Property(e => e.Width).HasPrecision(10, 2);
            entity.Property(e => e.Height).HasPrecision(10, 2);
            entity.Property(e => e.PositionX).HasPrecision(10, 2);
            entity.Property(e => e.PositionY).HasPrecision(10, 2);
            entity.HasIndex(e => new { e.CustomOrderId, e.SortOrder });
            entity.HasOne(e => e.CustomOrder).WithMany(o => o.Designs).HasForeignKey(e => e.CustomOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(entity =>
        {
            entity.Property(e => e.ContactName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ContactPhone).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ContactEmail).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ShippingCity).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ShippingAddressLine1).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ShippingAddressLine2).HasMaxLength(200);
            entity.Property(e => e.ShippingPostalCode).HasMaxLength(20);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            entity.Property(e => e.PromoDiscountAmount).HasPrecision(18, 2);
            entity.Property(e => e.PromoDiscountPercentage).HasPrecision(5, 2);
            entity.Property(e => e.PromoCode).HasMaxLength(64);
            entity.Property(e => e.ShippingCost).HasPrecision(18, 2);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.BogOrderId).HasMaxLength(100);
            entity.Property(e => e.BogOrderKey).HasMaxLength(64);
            entity.Property(e => e.CustomerNotes).HasMaxLength(1000);
            entity.Property(e => e.AdminNotes).HasMaxLength(1000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.BogOrderId);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        builder.Entity<OrderItem>(entity =>
        {
            entity.Property(e => e.ProductName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.ProductSlug).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ProductImageUrl).HasMaxLength(500);
            entity.Property(e => e.VariantName).HasMaxLength(200);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.LineTotal).HasPrecision(18, 2);
            entity.HasOne(e => e.Order).WithMany(o => o.Items).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        builder.Entity<OrderStatusLog>(entity =>
        {
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ChangedAt);
            entity.HasOne(e => e.Order).WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PendingEmail>(entity =>
        {
            entity.Property(e => e.ToEmail).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(300).IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasIndex(e => new { e.Status, e.NextRetryAt });
        });

        builder.Entity<PromoCode>(entity =>
        {
            entity.Property(e => e.Code).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DiscountPercentage).HasPrecision(5, 2);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        builder.Entity<Cart>(entity =>
        {
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Items).WithOne(i => i.Cart).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CartItem>(entity =>
        {
            entity.Property(e => e.Quantity).IsRequired();
            entity.Property(e => e.VariantId).HasDefaultValue(0).IsRequired();
            entity.HasIndex(e => new { e.CartId, e.ProductId, e.VariantId }).IsUnique();
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
