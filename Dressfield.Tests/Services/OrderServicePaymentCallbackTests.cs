using Dressfield.Core.Enums;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dressfield.Tests.Services;

public sealed class OrderServicePaymentCallbackTests : IDisposable
{
    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly FakePaymentService _payment = new();
    private readonly OrderService _orders;

    public OrderServicePaymentCallbackTests()
    {
        (_db, _conn) = TestDb.Create();
        _orders = new OrderService(
            _db, _payment, new CartService(_db), TestConfig.Build(),
            NullLogger<OrderService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ApprovedCallback_WithMatchingAmount_MarksPaidLogsAndQueuesEmail()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-1", "txn-1", "completed", 65m, "GEL");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, reloaded.Status);

        var log = await _db.OrderStatusLogs.SingleAsync(l => l.OrderId == order.Id);
        Assert.Equal(OrderStatus.PaymentProcessing, log.FromStatus);
        Assert.Equal(OrderStatus.Paid, log.ToStatus);

        // Email must go through the outbox, never direct SMTP
        var email = await _db.PendingEmails.SingleAsync();
        Assert.Equal("test@test.local", email.ToEmail);
    }

    [Fact]
    public async Task ApprovedCallback_WithAmountMismatch_CancelsWithReason()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-1", "txn-1", "completed", 0.01m, "GEL");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        var log = await _db.OrderStatusLogs.SingleAsync(l => l.OrderId == order.Id);
        Assert.Equal(OrderStatus.Cancelled, log.ToStatus);
        Assert.Contains("amount", log.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_db.PendingEmails);
    }

    [Fact]
    public async Task ApprovedCallback_WithWrongCurrency_Cancels()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-1", "txn-1", "completed", 65m, "USD");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task TransientFailure_ResetsToAwaitingPayment_ForReaperRetry()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(false, "bog-1", null, "network_error", IsTransientFailure: true);

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, reloaded.Status);
        Assert.Empty(_db.PendingEmails);
    }

    [Fact]
    public async Task PendingBogStatus_ResetsToAwaitingPayment()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(false, "bog-1", null, "processing");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, reloaded.Status);
    }

    [Fact]
    public async Task DeclinedCallback_CancelsAndRestoresStockAndPromo()
    {
        var variant = TestData.SeedVariant(_db, stock: 8); // 8 left after 2 were reserved
        var promo = TestData.SeedPromo(_db, usedCount: 1);
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment,
            total: 100m, variant: variant, quantity: 2, promoCode: promo.Code);
        _payment.NextVerification = new(false, "bog-1", "txn-1", "rejected");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        var reloaded = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        var stockAfter = await _db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == variant.Id);
        Assert.Equal(10, stockAfter.StockQuantity);

        var promoAfter = await _db.PromoCodes.AsNoTracking().SingleAsync(p => p.Id == promo.Id);
        Assert.Equal(0, promoAfter.UsedCount);
    }

    [Fact]
    public async Task SecondCallback_ForSameOrder_IsNoOp()
    {
        var order = TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, total: 65m);
        _payment.NextVerification = new(true, "bog-1", "txn-1", "completed", 65m, "GEL");

        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);
        await _orders.HandlePaymentCallbackAsync("bog-1", order.BogOrderKey);

        // The claim fails the second time: exactly one status log, one email, one verify call.
        Assert.Equal(1, await _db.OrderStatusLogs.CountAsync(l => l.OrderId == order.Id));
        Assert.Equal(1, await _db.PendingEmails.CountAsync());
        Assert.Single(_payment.VerifyCalls);
    }

    [Fact]
    public async Task Callback_ForUnknownBogOrderId_DoesNothing()
    {
        TestData.SeedOrder(_db, OrderStatus.AwaitingPayment, bogOrderId: "bog-known");

        await _orders.HandlePaymentCallbackAsync("bog-unknown", null);

        Assert.Empty(_payment.VerifyCalls);
        Assert.Empty(_db.OrderStatusLogs);
    }
}
