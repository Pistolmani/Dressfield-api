namespace Dressfield.Application.Options;

/// <summary>
/// Server-side floor for client-submitted custom order prices.
/// Bound from <c>CustomOrders:PriceFloor</c> in appsettings. Values mirror the frontend
/// pricing formula (basePrice per product type + embroidery fee per design) - if frontend
/// prices ever DROP, this config must be lowered too or legitimate orders get rejected.
/// Azure override example: CustomOrders__PriceFloor__BasePrices__hoodie.
/// </summary>
public class CustomOrderPricingOptions
{
    /// <summary>Kill switch - set false to disable floor enforcement without a redeploy.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base garment price per ProductTypeId ("hoodie", "tshirt", ...). Own-product types are 0.</summary>
    public Dictionary<string, decimal> BasePrices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Embroidery fee per size ("S".."3XL"). The floor uses the cheapest fee - sizes are client-supplied.</summary>
    public Dictionary<string, decimal> EmbroiderySizeFees { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
