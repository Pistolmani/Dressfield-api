using Dressfield.Application.Interfaces;
using Dressfield.Application.Options;
using Dressfield.Core.Entities;
using Dressfield.Core.Enums;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dressfield.Tests.Services;

public sealed class AbandonedOrderReaperTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _provider;
    private readonly FakePaymentService _payment = new();
    private readonly DressfieldDbContext _seedDb;
    private readonly AbandonedOrderReaper _reaper;

    public AbandonedOrderReaperTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var config = TestConfig.Build(
            ("Orders:AbandonmentTimeoutMinutes", "30"),
            ("Orders:PendingTimeoutMinutes", "10"),
            ("Orders:PaymentProcessingTimeoutMinutes", "15"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<DressfieldDbContext>(o => o.UseSqlite(_conn));
        services.AddSingleton<IPaymentService>(_payment);
        services.AddSingleton<IStorageService, FakeStorageService>();
        services.AddSingleton(Options.Create(new CustomOrderPricingOptions()));
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICustomOrderService, CustomOrderService>();
        _provider = services.BuildServiceProvider();

        // Create the schema once on the shared connection
        _seedDb = new DressfieldDbContext(TestDb.BuildOptions(_conn));
        _seedDb.Database.EnsureCreated();

        _reaper = new AbandonedOrderReaper(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AbandonedOrderReaper>.Instance,
            config);
    }

    public void Dispose()
    {
        _seedDb.Dispose();
        _provider.Dispose();
        _conn.Dispose();
    }

    private static DateTime HourAgo => DateTime.UtcNow.AddHours(-1);

    [Fact]
    public async Task StalePendingOrder_WithNoBogRecord_IsCancelledAndStockRestored()
    {
        var variant = TestData.SeedVariant(_seedDb, stock: 9);
        var order = TestData.SeedOrder(_seedDb, OrderStatus.Pending,
            bogOrderId: null, variant: variant, createdAt: HourAgo, updatedAt: HourAgo);
        _payment.NextLookup = null; // BOG has no record

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        var stock = await _seedDb.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == variant.Id);
        Assert.Equal(10, stock.StockQuantity);
    }

    [Fact]
    public async Task StalePendingOrder_WhenBogLookupTransient_IsLeftUntouched()
    {
        var order = TestData.SeedOrder(_seedDb, OrderStatus.Pending,
            bogOrderId: null, createdAt: HourAgo, updatedAt: HourAgo);
        _payment.NextLookup = new(false, "", null, "error", IsTransientFailure: true);

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Pending, reloaded.Status);
    }

    [Fact]
    public async Task StalePendingOrder_WhenBogHasSession_IsHydratedToAwaitingPayment()
    {
        var order = TestData.SeedOrder(_seedDb, OrderStatus.Pending,
            bogOrderId: null, createdAt: HourAgo, updatedAt: HourAgo);
        // BOG knows the session but it's not terminal -> hydrate, don't cancel
        _payment.NextLookup = new(false, "bog-recovered", null, "created");
        _payment.NextVerification = new(false, "bog-recovered", null, "created");

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, reloaded.Status);
        Assert.Equal("bog-recovered", reloaded.BogOrderId);
    }

    [Fact]
    public async Task StuckPaymentProcessingOrder_PastTimeout_IsResetToAwaitingPayment()
    {
        var order = TestData.SeedOrder(_seedDb, OrderStatus.PaymentProcessing, updatedAt: HourAgo);
        // After the reset, the abandoned pass will verify with BOG; keep it inconclusive
        _payment.NextVerification = new(false, "bog-1", null, "processing");

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, reloaded.Status);
    }

    [Fact]
    public async Task AbandonedOrder_NotPaidAtBog_IsCancelledWithStockAndPromoRestored()
    {
        var variant = TestData.SeedVariant(_seedDb, stock: 9);
        var promo = TestData.SeedPromo(_seedDb, usedCount: 1);
        var order = TestData.SeedOrder(_seedDb, OrderStatus.AwaitingPayment,
            variant: variant, promoCode: promo.Code, updatedAt: HourAgo);
        _payment.NextVerification = new(false, "bog-1", null, "rejected");

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        var stock = await _seedDb.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == variant.Id);
        Assert.Equal(10, stock.StockQuantity);

        var promoAfter = await _seedDb.PromoCodes.AsNoTracking().SingleAsync(p => p.Id == promo.Id);
        Assert.Equal(0, promoAfter.UsedCount);
    }

    [Fact]
    public async Task AbandonedOrder_ActuallyPaidAtBog_IsResolvedAsPaidNotCancelled()
    {
        var order = TestData.SeedOrder(_seedDb, OrderStatus.AwaitingPayment,
            total: 65m, updatedAt: HourAgo);
        _payment.NextVerification = new(true, "bog-1", "txn-1", "completed", 65m, "GEL");

        await _reaper.RunAsync(CancellationToken.None);

        var reloaded = await _seedDb.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, reloaded.Status);
    }

    [Fact]
    public async Task ExpiredAndRevokedRefreshTokens_ArePurged()
    {
        var user = new ApplicationUser
        {
            UserName = "purge@test.local",
            Email = "purge@test.local",
            FirstName = "P",
            LastName = "U",
        };
        _seedDb.Users.Add(user);
        _seedDb.RefreshTokens.AddRange(
            new RefreshToken { Token = "expired", UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(-1) },
            new RefreshToken { Token = "revoked", UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(5), IsRevoked = true },
            new RefreshToken { Token = "live", UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(5) });
        await _seedDb.SaveChangesAsync();
        _seedDb.ChangeTracker.Clear();

        await _reaper.RunAsync(CancellationToken.None);

        var remaining = await _seedDb.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("live", remaining[0].Token);
    }
}
