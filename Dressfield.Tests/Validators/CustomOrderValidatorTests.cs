using Dressfield.Application.DTOs;
using Dressfield.Application.Validators;
using Microsoft.Extensions.Options;

namespace Dressfield.Tests.Validators;

public sealed class CustomOrderValidatorTests
{
    private static CreateCustomOrderRequestValidator Build(params string[] allowedHosts) =>
        new(Options.Create(new UploadHostOptions { AllowedHosts = allowedHosts }));

    private static CreateCustomOrderRequest Request(string designUrl, decimal totalPrice = 65m) =>
        new(
            BaseProductId: null,
            ContactName: "Test",
            ContactPhone: "555123456",
            ContactEmail: "t@t.local",
            CustomerNotes: null,
            TotalPrice: totalPrice,
            ProductTypeId: "hoodie",
            ColorHex: null,
            ClothingSize: null,
            CanvasWidth: 500,
            CanvasHeight: 500,
            Designs:
            [
                new CreateCustomOrderDesignRequest(
                    designUrl, "chest", "S", null, "front", null, null, null, null, null, null, null, 0),
            ]);

    [Fact]
    public void AllowedHost_Passes()
    {
        var result = Build("storage.test").Validate(Request("https://storage.test/designs/d.webp"));
        Assert.True(result.IsValid, string.Join(";", result.Errors));
    }

    [Fact]
    public void ForeignHost_IsRejected()
    {
        var result = Build("storage.test").Validate(Request("https://evil.example/designs/d.webp"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void EmptyAllowlist_FailsClosed()
    {
        var result = Build().Validate(Request("https://storage.test/designs/d.webp"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void HttpScheme_IsRejected()
    {
        var result = Build("storage.test").Validate(Request("http://storage.test/designs/d.webp"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonPositiveTotalPrice_IsRejected()
    {
        var result = Build("storage.test").Validate(
            Request("https://storage.test/designs/d.webp", totalPrice: 0m));
        Assert.False(result.IsValid);
    }
}
