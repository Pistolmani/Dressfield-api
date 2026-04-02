namespace Dressfield.Application.DTOs;

public record AdminDashboardSummaryDto(
    int TotalOrders,
    decimal TotalRevenue,
    int PaidTodayCount,
    int PendingCustomOrdersCount);

