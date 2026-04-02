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

        var totalOrdersTask = _db.Orders
            .AsNoTracking()
            .CountAsync();

        var totalRevenueTask = _db.Orders
            .AsNoTracking()
            .Where(o => RevenueStatuses.Contains(o.Status))
            .SumAsync(o => (decimal?)o.TotalAmount);

        var paidTodayCountTask = _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.Status == OrderStatus.Paid &&
                o.UpdatedAt >= startOfTodayUtc &&
                o.UpdatedAt < startOfTomorrowUtc)
            .CountAsync();

        var pendingCustomOrdersCountTask = _db.CustomOrders
            .AsNoTracking()
            .CountAsync(o => PendingCustomStatuses.Contains(o.Status));

        await Task.WhenAll(totalOrdersTask, totalRevenueTask, paidTodayCountTask, pendingCustomOrdersCountTask);

        return new AdminDashboardSummaryDto(
            totalOrdersTask.Result,
            totalRevenueTask.Result ?? 0m,
            paidTodayCountTask.Result,
            pendingCustomOrdersCountTask.Result);
    }
}

