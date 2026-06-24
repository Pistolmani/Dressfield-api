using Dressfield.Application.DTOs;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dressfield.Tests.Services;

public sealed class PromoCodeServiceValidateTests : IDisposable
{
    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly PromoCodeService _promos;

    public PromoCodeServiceValidateTests()
    {
        (_db, _conn) = TestDb.Create();
        _promos = new PromoCodeService(_db, NullLogger<PromoCodeService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ValidCode_ReturnsDiscount()
    {
        TestData.SeedPromo(_db, code: "SAVE10", percent: 10m);

        var result = await _promos.ValidateAsync(new ValidatePromoCodeRequest("save10", 100m));

        Assert.True(result.IsValid);
        Assert.Equal("SAVE10", result.Code);
        Assert.Equal(10m, result.DiscountPercentage);
        Assert.Equal(10m, result.DiscountAmount);
    }

    [Fact]
    public async Task UnknownOrInactiveCode_IsInvalid()
    {
        TestData.SeedPromo(_db, code: "OFF", isActive: false);

        Assert.False((await _promos.ValidateAsync(new ValidatePromoCodeRequest("NOPE", 100m))).IsValid);
        Assert.False((await _promos.ValidateAsync(new ValidatePromoCodeRequest("OFF", 100m))).IsValid);
    }

    [Fact]
    public async Task ExpiredCode_IsInvalid()
    {
        TestData.SeedPromo(_db, code: "OLD", expiresAtUtc: DateTime.UtcNow.AddDays(-1));

        var result = await _promos.ValidateAsync(new ValidatePromoCodeRequest("OLD", 100m));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ExhaustedCode_IsInvalid()
    {
        TestData.SeedPromo(_db, code: "FULL", maxUses: 3, usedCount: 3);

        var result = await _promos.ValidateAsync(new ValidatePromoCodeRequest("FULL", 100m));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task EmptySubtotal_IsInvalid()
    {
        TestData.SeedPromo(_db, code: "SAVE10");

        var result = await _promos.ValidateAsync(new ValidatePromoCodeRequest("SAVE10", 0m));

        Assert.False(result.IsValid);
    }
}
