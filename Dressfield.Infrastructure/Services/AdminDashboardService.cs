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
        var totalOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync();

        var totalRevenue = await _db.Orders
            .AsNoTracking()
            .Where(o => RevenueStatuses.Contains(o.Status))
            .SumAsync(o => (decimal?)o.TotalAmount);

        var paidTodayCount = await _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.Status == OrderStatus.Paid &&
                o.UpdatedAt >= startOfTodayUtc &&
                o.UpdatedAt < startOfTomorrowUtc)
            .CountAsync();

        var pendingCustomOrdersCount = await _db.CustomOrders
            .AsNoTracking()
            .CountAsync(o => PendingCustomStatuses.Contains(o.Status));

        return new AdminDashboardSummaryDto(
            totalOrders,
            totalRevenue ?? 0m,
            paidTodayCount,
            pendingCustomOrdersCount);
    }
}

