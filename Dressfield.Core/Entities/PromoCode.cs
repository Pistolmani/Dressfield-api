namespace Dressfield.Core.Entities;

public class PromoCode
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAtUtc { get; set; }

    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public int? MaxUsesPerUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
