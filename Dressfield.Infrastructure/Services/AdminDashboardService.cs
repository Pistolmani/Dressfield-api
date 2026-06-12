using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Enums;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dressfield.Infrastructure.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private static readonly OrderStatus[] RevenueStatuses =
    [
        OrderStatus.Paid,
        OrderStatus.Processing,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
    ];

    // Custom orders flip to Reviewing the moment BOG confirms payment, then move
    // through Approved -> InProduction -> Completed as the admin works the order.
    // All four states are post-payment, so all four count toward revenue.
    private static readonly CustomOrderStatus[] CustomOrderRevenueStatuses =
    [
        CustomOrderStatus.Reviewing,
        CustomOrderStatus.Approved,
        CustomOrderStatus.InProduction,
        CustomOrderStatus.Completed,
    ];

    private static readonly CustomOrderStatus[] PendingCustomStatuses =
    [
        CustomOrderStatus.Pending,
        CustomOrderStatus.Reviewing,
    ];

    private readonly DressfieldDbContext _db;

    public AdminDashboardService(DressfieldDbContext db)
    {
        _db = db;
    }

    public async Task<AdminDashboardSummaryDto> GetSummaryAsync()
    {
        var startOfTodayUtc = DateTime.UtcNow.Date;
        var startOfTomorrowUtc = startOfTodayUtc.AddDays(1);

        // DbContext is not thread-safe; running these as Task.WhenAll on a single
        // scoped context throws "A second operation was started on this context..."
        // which the global handler maps to 400. Await sequentially — these are
        // small aggregate queries and there is no real perf win from parallelism.
        // Regular orders
        var regularOrdersCount = await _db.Orders
            .AsNoTracking()
            .CountAsync();

        var regularRevenue = await _db.Orders
            .AsNoTracking()
            .Where(o => RevenueStatuses.Contains(o.Status))
            .SumAsync(o => (decimal?)o.TotalAmount);

        var regularPaidToday = await _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.Status == OrderStatus.Paid &&
                o.UpdatedAt >= startOfTodayUtc &&
                o.UpdatedAt < startOfTomorrowUtc)
            .CountAsync();

        // Custom orders — historically excluded from revenue, which made the
        // dashboard underreport whenever a customer paid for an embroidery order.
        var customOrdersCount = await _db.CustomOrders
            .AsNoTracking()
            .CountAsync();

        var customRevenue = await _db.CustomOrders
            .AsNoTracking()
            .Where(o => CustomOrderRevenueStatuses.Contains(o.Status))
            .SumAsync(o => (decimal?)o.TotalPrice);

        // "Paid today" counts the moment a custom order transitions out of the
        // payment-processing zone into Reviewing on or after today (UTC).
        var customPaidToday = await _db.CustomOrders
            .AsNoTracking()
            .Where(o =>
                o.Status == CustomOrderStatus.Reviewing &&
                o.UpdatedAt >= startOfTodayUtc &&
                o.UpdatedAt < startOfTomorrowUtc)
            .CountAsync();

        var pendingCustomOrdersCount = await _db.CustomOrders
            .AsNoTracking()
            .CountAsync(o => PendingCustomStatuses.Contains(o.Status));

        return new AdminDashboardSummaryDto(
            regularOrdersCount + customOrdersCount,
            (regularRevenue ?? 0m) + (customRevenue ?? 0m),
            regularPaidToday + customPaidToday,
            pendingCustomOrdersCount);
    }
}

