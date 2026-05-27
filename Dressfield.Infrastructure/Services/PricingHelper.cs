namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Shared pricing utilities used across OrderService, CartService, and PromoCodeService.
/// </summary>
internal static class PricingHelper
{
    /// <summary>Clamp a percentage value to [0, 100].</summary>
    public static decimal NormalizePercent(decimal value) =>
        Math.Clamp(value, 0m, 100m);

    /// <summary>Round a monetary value to 2 decimal places (banker's rounding away from zero).</summary>
    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Apply a sale percentage to a base price and return the discounted price.</summary>
    public static decimal CalculateDiscountedPrice(decimal basePrice, decimal salePercentage)
    {
        var percent = NormalizePercent(salePercentage);
        return RoundMoney(basePrice * (1m - percent / 100m));
    }
}
