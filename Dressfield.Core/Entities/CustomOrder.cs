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

    // Garment context — populated so the admin preview can re-render the customer's design
    // on top of the same product silhouette + color they configured at checkout.
    public string? ProductTypeId { get; set; }   // "hoodie" | "tshirt" | "cap" | etc.
    public string? ColorHex { get; set; }        // garment color e.g. "#000000"
    public string? ClothingSize { get; set; }    // "S" | "M" | "L" | "XL"
    public int? CanvasWidth { get; set; }        // canvas px the customer placed designs on
    public int? CanvasHeight { get; set; }

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
