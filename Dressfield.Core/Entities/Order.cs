using Dressfield.Core.Enums;

namespace Dressfield.Core.Entities;

public class Order
{
    public int Id { get; set; }

    // Nullable — guest checkout supported
    public string? UserId { get; set; }

    // Contact snapshot (denormalised — survives user deletion)
    public string ContactName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    // Shipping address snapshot
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingAddressLine1 { get; set; } = string.Empty;
    public string? ShippingAddressLine2 { get; set; }
    public string? ShippingPostalCode { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Pricing snapshot
    public decimal Subtotal { get; set; }
    public decimal PromoDiscountAmount { get; set; }
    public decimal? PromoDiscountPercentage { get; set; }
    public string? PromoCode { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal TotalAmount { get; set; }

    // BOG iPay fields
    public string? BogOrderId { get; set; }      // ID returned by BOG when creating a payment session
    public string? BogOrderKey { get; set; }     // Unique key we generate and send to BOG

    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
