using Dressfield.Core.Enums;

namespace Dressfield.Core.Entities;

public class CustomOrderStatusLog
{
    public int Id { get; set; }
    public int CustomOrderId { get; set; }

    public CustomOrderStatus FromStatus { get; set; }
    public CustomOrderStatus ToStatus { get; set; }

    public string? ChangedByUserId { get; set; }

    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public CustomOrder CustomOrder { get; set; } = null!;
}
