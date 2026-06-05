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

    // Which side of the garment the design sits on. Distinct from Placement (the zone).
    public string? Side { get; set; }         // "front" | "back"

    // Canvas geometry — Fabric.js coordinates so the admin preview can re-render the
    // exact composition the customer saw.
    //  - PositionX/Y: center of the design in canvas pixels (Fabric stores center coords)
    //  - Width/Height: rendered px on canvas after scaling (= natural × scale)
    //  - ScaleX/Y: scaling factor applied to the natural image dimensions
    //  - Angle: clockwise rotation in degrees
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? PositionX { get; set; }
    public decimal? PositionY { get; set; }
    public decimal? ScaleX { get; set; }
    public decimal? ScaleY { get; set; }
    public decimal? Angle { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public CustomOrder CustomOrder { get; set; } = null!;
}
