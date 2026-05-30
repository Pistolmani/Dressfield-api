using Dressfield.Core.Pricing;
using Xunit;

namespace Dressfield.Tests;

public class ShippingPricingTests
{
    private const decimal Tbilisi = 5m;
    private const decimal Other = 15m;

    [Theory]
    [InlineData("Tbilisi")]
    [InlineData("tbilisi")]
    [InlineData("TBILISI")]
    [InlineData("  Tbilisi  ")]
    [InlineData("თბილისი")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_ReturnsTbilisiRate_ForTbilisiOrEmpty(string? city)
    {
        Assert.Equal(Tbilisi, ShippingPricing.Resolve(city, Tbilisi, Other));
    }

    [Theory]
    [InlineData("Batumi")]
    [InlineData("ბათუმი")]
    [InlineData("KUTAISI")]
    [InlineData("Rustavi")]
    public void Resolve_ReturnsOtherRate_ForOtherCities(string city)
    {
        Assert.Equal(Other, ShippingPricing.Resolve(city, Tbilisi, Other));
    }
}
