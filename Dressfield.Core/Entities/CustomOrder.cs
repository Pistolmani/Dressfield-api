using Dressfield.Core.Enums;

namespace Dressfield.Core.Entities;

public class CustomOrder
{
    public int Id { get; set; }

    // Nullable — guest orders are supported
    public string? UserId { get; set; }

    // Nullable — customer can order from a base product or submit a blank canvas
    public int? BaseProductId { get; set; }

    public string ContactName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    public CustomOrderStatus Status { get; set; } = CustomOrderStatus.Pending;

    public decimal TotalPrice { get; set; }

    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }

    // BOG iPay payment fields
    public string? BogOrderKey { get; set; }
    public string? BogOrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public Product? BaseProduct { get; set; }
    public ICollection<CustomOrderDesign> Designs { get; set; } = new List<CustomOrderDesign>();
}
