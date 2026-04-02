using Dressfield.Core.Enums;

namespace Dressfield.Core.Entities;

public class OrderStatusLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }

    /// <summary>UserId of the admin who made the change, or null for system events (payment callback).</summary>
    public string? ChangedByUserId { get; set; }

    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
}
