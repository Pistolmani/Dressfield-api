namespace Dressfield.Core.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    // Nullable — product may be deleted after order is placed
    public int? ProductId { get; set; }

    // Snapshot fields — frozen at time of purchase
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public string? VariantName { get; set; }   // e.g. "ზომა: L"
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Product? Product { get; set; }
}
