namespace Dressfield.Core.Entities;

public class CustomOrderDesign
{
    public int Id { get; set; }
    public int CustomOrderId { get; set; }

    public string DesignImageUrl { get; set; } = string.Empty;

    // Canvas placement options — all nullable, filled by the canvas editor in Phase 3 Plan 4
    public string? Placement { get; set; }   // "chest" | "back" | "sleeve" | "full-front"
    public string? Size { get; set; }         // "S" | "M" | "L" | "XL"
    public string? ThreadColor { get; set; }  

    // Canvas geometry — percentage-based position & cm dimensions
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? PositionX { get; set; }
    public decimal? PositionY { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public CustomOrder CustomOrder { get; set; } = null!;
}
