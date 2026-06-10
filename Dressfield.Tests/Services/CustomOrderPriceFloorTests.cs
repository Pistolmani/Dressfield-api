using Dressfield.Application.DTOs;
using Dressfield.Application.Options;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dressfield.Tests.Services;

public sealed class CustomOrderPriceFloorTests : IDisposable
{
    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;

    public CustomOrderPriceFloorTests()
    {
        (_db, _conn) = TestDb.Create();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private CustomOrderService BuildService(bool enabled = true)
    {
        var options = new CustomOrderPricingOptions
        {
            Enabled = enabled,
            BasePrices = new(StringComparer.OrdinalIgnoreCase) { ["hoodie"] = 50m, ["custom"] = 0m },
            EmbroiderySizeFees = new(StringComparer.OrdinalIgnoreCase)
                { ["S"] = 15m, ["M"] = 20m, ["L"] = 30m },
        };
        return new CustomOrderService(
            _db, new FakePaymentService(), new FakeStorageService(),
            Options.Create(options), NullLogger<CustomOrderService>.Instance);
    }

    private static CreateCustomOrderRequest Request(decimal totalPrice, string? productTypeId, int designs = 1) =>
        new(
            BaseProductId: null,
            ContactName: "Test",
            ContactPhone: "555123456",
            ContactEmail: "t@t.local",
            CustomerNotes: null,
            TotalPrice: totalPrice,
            ProductTypeId: productTypeId,
            ColorHex: null,
            ClothingSize: null,
            CanvasWidth: 500,
            CanvasHeight: 500,
            Designs: Enumerable.Range(0, designs)
                .Select(i => new CreateCustomOrderDesignRequest(
                    $"https://storage.test/designs/d{i}.webp",
                    "chest", "S", null, "front", null, null, null, null, null, null, null, i))
                .ToList());

    [Fact]
    public async Task BelowFloor_Throws()
    {
        // hoodie floor: 50 + 15 x 1 = 65
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnforcePriceFloorAsync(Request(0.01m, "hoodie")));
    }

    [Fact]
    public async Task ExactlyAtFloor_Passes()
    {
        var svc = BuildService();
        await svc.EnforcePriceFloorAsync(Request(65m, "hoodie")); // no throw
    }

    [Fact]
    public async Task NullProductType_EnforcesMinFeePerDesign()
    {
        // floor: 0 + 15 x 2 = 30
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnforcePriceFloorAsync(Request(20m, null, designs: 2)));
        await svc.EnforcePriceFloorAsync(Request(30m, null, designs: 2)); // no throw
    }

    [Fact]
    public async Task ActivePromo_LowersTheFloor()
    {
        TestData.SeedPromo(_db, percent: 50m);
        var svc = BuildService();
        // hoodie floor 65 halves to 32.50
        await svc.EnforcePriceFloorAsync(Request(32.5m, "hoodie")); // no throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnforcePriceFloorAsync(Request(30m, "hoodie")));
    }

    [Fact]
    public async Task InactiveOrExhaustedPromos_DoNotLowerTheFloor()
    {
        TestData.SeedPromo(_db, code: "OFF", percent: 90m, isActive: false);
        TestData.SeedPromo(_db, code: "USED", percent: 90m, maxUses: 5, usedCount: 5);
        TestData.SeedPromo(_db, code: "EXPIRED", percent: 90m, expiresAtUtc: DateTime.UtcNow.AddDays(-1));

        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnforcePriceFloorAsync(Request(10m, "hoodie")));
    }

    [Fact]
    public async Task KillSwitch_BypassesTheCheck()
    {
        var svc = BuildService(enabled: false);
        await svc.EnforcePriceFloorAsync(Request(0.01m, "hoodie")); // no throw
    }

    [Fact]
    public async Task OwnProductType_FloorIsMinFeeOnly()
    {
        // "custom" base 0: floor = 15 x 1
        var svc = BuildService();
        await svc.EnforcePriceFloorAsync(Request(15m, "custom"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EnforcePriceFloorAsync(Request(5m, "custom")));
    }
}
