namespace Dressfield.Core.Pricing;

/// <summary>
/// Delivery pricing rule for regular (catalog) orders.
///
/// IMPORTANT: the frontend mirrors this exact rule in
/// Dressfield.web/src/lib/shipping.ts -> getShippingCostByCity().
/// If you change the city matching here, change it there too, or the amount
/// shown at checkout will drift from the amount charged at BOG.
/// </summary>
public static class ShippingPricing
{
    /// <summary>
    /// Tbilisi (any casing/spacing, Latin or Georgian) ships at <paramref name="tbilisiCost"/>;
    /// an empty city defaults to Tbilisi; everywhere else is <paramref name="otherCitiesCost"/>.
    /// </summary>
    public static decimal Resolve(string? city, decimal tbilisiCost, decimal otherCitiesCost)
    {
        var normalized = city?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized == string.Empty || normalized == "tbilisi" || normalized == "თბილისი")
            return tbilisiCost;
        return otherCitiesCost;
    }
}
