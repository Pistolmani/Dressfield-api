using Dressfield.Application.Options;
using Dressfield.Core.Enums;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dressfield.Tests.Services;

public sealed class CustomOrderServicePaymentCallbackTests : IDisposable
{
    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly FakePaymentService _payment = new();
    private readonly CustomOrderService _customOrders;

    public CustomOrderServicePaymentCallbackTests()
    {
        (_db, _conn) = TestDb.Create();
        _customOrders = new CustomOrderService(
            _db, _payment, new FakeStorageService(),
            Options.Create(new CustomOrderPricingOptions()),
            NullLogger<CustomOrderService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ApprovedCallback_MovesToReviewing_AndQueuesEmail()
    {
        var order = TestData.SeedCustomOrder(_db, CustomOrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-c1", "txn-1", "completed", 65m, "GEL");

        await _customOrders.HandlePaymentCallbackAsync("bog-c1", order.BogOrderKey);

        var reloaded = await _db.CustomOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(CustomOrderStatus.Reviewing, reloaded.Status);

        var email = await _db.PendingEmails.SingleAsync();
        Assert.Equal("custom@test.local", email.ToEmail);
    }

    [Fact]
    public async Task ApprovedCallback_WithAmountMismatch_Cancels()
    {
        var order = TestData.SeedCustomOrder(_db, CustomOrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-c1", "txn-1", "completed", 0.01m, "GEL");

        await _customOrders.HandlePaymentCallbackAsync("bog-c1", order.BogOrderKey);

        var reloaded = await _db.CustomOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(CustomOrderStatus.Cancelled, reloaded.Status);
        Assert.Empty(_db.PendingEmails);
    }

    [Fact]
    public async Task TransientFailure_ResetsToAwaitingPayment()
    {
        var order = TestData.SeedCustomOrder(_db, CustomOrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(false, "bog-c1", null, "timeout", IsTransientFailure: true);

        await _customOrders.HandlePaymentCallbackAsync("bog-c1", order.BogOrderKey);

        var reloaded = await _db.CustomOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(CustomOrderStatus.AwaitingPayment, reloaded.Status);
    }

    [Fact]
    public async Task SecondCallback_IsNoOp()
    {
        var order = TestData.SeedCustomOrder(_db, CustomOrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-c1", "txn-1", "completed", 65m, "GEL");

        await _customOrders.HandlePaymentCallbackAsync("bog-c1", order.BogOrderKey);
        await _customOrders.HandlePaymentCallbackAsync("bog-c1", order.BogOrderKey);

        Assert.Equal(1, await _db.PendingEmails.CountAsync());
        Assert.Single(_payment.VerifyCalls);
    }
}
