using Dressfield.Application.DTOs;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dressfield.Tests.Services;

public sealed class OrderServiceIdempotencyTests : IDisposable
{
    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly FakePaymentService _payment = new();
    private readonly OrderService _orders;

    public OrderServiceIdempotencyTests()
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

    private static CreateOrderRequest BuildRequest(ProductVariant variant) => new(
        ContactName: "Guest User",
        ContactPhone: "555000111",
        ContactEmail: "guest@example.com",
        ShippingCity: "Tbilisi",
        ShippingAddressLine1: "1 Test St",
        ShippingAddressLine2: null,
        ShippingPostalCode: null,
        PromoCode: null,
        CustomerNotes: null,
        Items: new[] { new CartItemRequest(variant.ProductId, variant.Id, 1) });

    [Fact]
    public async Task Guest_checkout_replay_with_same_key_returns_the_same_order()
    {
        var variant = TestData.SeedVariant(_db, stock: 10);

        var first  = await _orders.CreateAsync(BuildRequest(variant), userId: null, idempotencyKey: "guest-key-1");
        var second = await _orders.CreateAsync(BuildRequest(variant), userId: null, idempotencyKey: "guest-key-1");

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await _db.Orders.CountAsync());
    }

    [Fact]
    public async Task Guest_checkouts_with_different_keys_create_separate_orders()
    {
        var variant = TestData.SeedVariant(_db, stock: 10);

        var first  = await _orders.CreateAsync(BuildRequest(variant), userId: null, idempotencyKey: "guest-key-A");
        var second = await _orders.CreateAsync(BuildRequest(variant), userId: null, idempotencyKey: "guest-key-B");

        Assert.NotEqual(first.OrderId, second.OrderId);
        Assert.Equal(2, await _db.Orders.CountAsync());
    }
}
