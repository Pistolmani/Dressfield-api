namespace Dressfield.Core.Entities;

public class AuditLog
{
    public int Id { get; set; }

    /// <summary>e.g. "ProductCreated", "OrderStatusChanged", "PromoCodeDeleted"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>e.g. "Product", "Order", "CustomOrder", "PromoCode"</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the affected entity.</summary>
    public string? EntityId { get; set; }

    /// <summary>Human-readable label: product name, order number, promo code, etc.</summary>
    public string? EntityName { get; set; }

    /// <summary>UserId of the admin who performed the action.</summary>
    public string? ActorId { get; set; }

    public string? ActorEmail { get; set; }

    /// <summary>Free-text context: "Status: Pending → Shipped", field diffs, etc.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
