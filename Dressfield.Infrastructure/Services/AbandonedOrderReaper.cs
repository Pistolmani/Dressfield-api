using Dressfield.Core.Entities;
using Dressfield.Core.Enums;
using Dressfield.Core.Interfaces;
using Dressfield.Application.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Background service that cancels orders stuck in AwaitingPayment after a configurable timeout
/// and restores any reserved stock and promo-code usage.
/// </summary>
public class AbandonedOrderReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbandonedOrderReaper> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _abandonmentTimeout;
    // Orders stuck in Pending (BOG session never created) are cleaned up faster
    private readonly TimeSpan _pendingTimeout;

    public AbandonedOrderReaper(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedOrderReaper> logger,
        IConfiguration config)
    {
        _scopeFactory       = scopeFactory;
        _logger             = logger;
        _interval           = TimeSpan.FromMinutes(config.GetValue("Orders:ReaperIntervalMinutes", 5));
        _abandonmentTimeout = TimeSpan.FromMinutes(config.GetValue("Orders:AbandonmentTimeoutMinutes", 30));
        _pendingTimeout     = TimeSpan.FromMinutes(config.GetValue("Orders:PendingTimeoutMinutes", 10));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AbandonedOrderReaper encountered an unexpected error");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DressfieldDbContext>();
        var payment = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var customOrders = scope.ServiceProvider.GetRequiredService<ICustomOrderService>();

        var now = DateTime.UtcNow;
        var abandonmentCutoff = now - _abandonmentTimeout;
        var pendingCutoff     = now - _pendingTimeout;

        await CancelStalePendingOrdersAsync(db, pendingCutoff, ct);
        await CancelStalePendingCustomOrdersAsync(db, pendingCutoff, ct);
        await CancelAbandonedOrdersAsync(db, payment, orders, abandonmentCutoff, ct);
        await CancelAbandonedCustomOrdersAsync(db, payment, customOrders, abandonmentCutoff, ct);
    }

    /// <summary>
    /// Cancels regular orders stuck in <see cref="OrderStatus.Pending"/> — meaning the BOG session
    /// was never created (or the process crashed before saving <c>BogOrderId</c>).
    /// These have no payment session so stock would be reserved indefinitely without this cleanup.
    /// </summary>
    private async Task CancelStalePendingOrdersAsync(DressfieldDbContext db, DateTime cutoff, CancellationToken ct)
    {
        var staleIds = await db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < cutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var id in staleIds)
        {
            var claimed = await db.Orders
                .Where(o => o.Id == id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.Status, OrderStatus.Cancelled)
                          .SetProperty(o => o.UpdatedAt, DateTime.UtcNow),
                    ct);

            if (claimed == 0)
                continue;

            var order = await db.Orders
                .Include(o => o.Items)
                .FirstAsync(o => o.Id == id, ct);

            db.OrderStatusLogs.Add(new OrderStatusLog
            {
                OrderId    = order.Id,
                FromStatus = OrderStatus.Pending,
                ToStatus   = OrderStatus.Cancelled,
                Notes      = "Cancelled — BOG payment session was never created (startup crash or BOG timeout)",
            });

            foreach (var item in order.Items.Where(i => i.VariantId.HasValue))
            {
                await db.ProductVariants
                    .Where(v => v.Id == item.VariantId!.Value)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(v => v.StockQuantity, v => v.StockQuantity + item.Quantity),
                        ct);
            }

            if (!string.IsNullOrEmpty(order.PromoCode))
            {
                await db.PromoCodes
                    .Where(p => p.Code == order.PromoCode && p.UsedCount > 0)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1),
                        ct);
            }

            await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Stale Pending order {OrderId} cancelled and stock restored (created at {CreatedAt})",
                order.Id, order.CreatedAt);
        }
    }

    /// <summary>
    /// Cancels custom orders stuck in <see cref="CustomOrderStatus.Pending"/> for the same reason.
    /// Custom orders have no stock to restore but still need to be cleaned up.
    /// </summary>
    private async Task CancelStalePendingCustomOrdersAsync(DressfieldDbContext db, DateTime cutoff, CancellationToken ct)
    {
        var staleIds = await db.CustomOrders
            .Where(o => o.Status == CustomOrderStatus.Pending && o.CreatedAt < cutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var id in staleIds)
        {
            var claimed = await db.CustomOrders
                .Where(o => o.Id == id && o.Status == CustomOrderStatus.Pending)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.Status, CustomOrderStatus.Cancelled)
                          .SetProperty(o => o.UpdatedAt, DateTime.UtcNow),
                    ct);

            if (claimed == 0)
                continue;

            db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = id,
                FromStatus    = CustomOrderStatus.Pending,
                ToStatus      = CustomOrderStatus.Cancelled,
                Notes         = "Cancelled — BOG payment session was never created (startup crash or BOG timeout)",
            });

            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Stale Pending custom order {OrderId} cancelled", id);
        }
    }

    private async Task CancelAbandonedOrdersAsync(
        DressfieldDbContext db,
        IPaymentService payment,
        IOrderService orders,
        DateTime cutoff,
        CancellationToken ct)
    {
        var candidateIds = await db.Orders
            .Where(o => o.Status == OrderStatus.AwaitingPayment && o.UpdatedAt < cutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var id in candidateIds)
        {
            var paymentState = await db.Orders
                .AsNoTracking()
                .Where(o => o.Id == id && o.Status == OrderStatus.AwaitingPayment)
                .Select(o => new { o.BogOrderId, o.BogOrderKey })
                .FirstOrDefaultAsync(ct);

            if (paymentState is null)
                continue;

            if (!string.IsNullOrWhiteSpace(paymentState.BogOrderId))
            {
                var verification = await payment.VerifyCallbackAsync(paymentState.BogOrderId);
                if (verification.IsApproved)
                {
                    await orders.HandlePaymentCallbackAsync(paymentState.BogOrderId, paymentState.BogOrderKey);
                    continue;
                }

                if (BogPaymentStatus.IsPending(verification.Status))
                {
                    _logger.LogInformation(
                        "Skipping abandoned cancellation for order {OrderId}; BOG status is still {Status} (BOG: {BogOrderId})",
                        id, verification.Status, paymentState.BogOrderId);
                    continue;
                }
            }

            // Atomic claim: if a concurrent callback already claimed this order, skip it
            var claimed = await db.Orders
                .Where(o => o.Id == id && o.Status == OrderStatus.AwaitingPayment)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.Status, OrderStatus.Cancelled)
                          .SetProperty(o => o.UpdatedAt, DateTime.UtcNow),
                    ct);

            if (claimed == 0)
                continue;

            var order = await db.Orders
                .Include(o => o.Items)
                .FirstAsync(o => o.Id == id, ct);

            db.OrderStatusLogs.Add(new OrderStatusLog
            {
                OrderId = order.Id,
                FromStatus = OrderStatus.AwaitingPayment,
                ToStatus = OrderStatus.Cancelled,
                Notes = $"Abandoned — payment not confirmed within {_abandonmentTimeout.TotalMinutes:F0} minutes",
            });

            foreach (var item in order.Items.Where(i => i.VariantId.HasValue))
            {
                await db.ProductVariants
                    .Where(v => v.Id == item.VariantId!.Value)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(v => v.StockQuantity, v => v.StockQuantity + item.Quantity),
                        ct);
            }

            if (!string.IsNullOrEmpty(order.PromoCode))
            {
                await db.PromoCodes
                    .Where(p => p.Code == order.PromoCode && p.UsedCount > 0)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1),
                        ct);
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Abandoned order {OrderId} cancelled and resources restored (BOG: {BogOrderId})",
                order.Id, order.BogOrderId);
        }
    }

    private async Task CancelAbandonedCustomOrdersAsync(
        DressfieldDbContext db,
        IPaymentService payment,
        ICustomOrderService customOrders,
        DateTime cutoff,
        CancellationToken ct)
    {
        var candidateIds = await db.CustomOrders
            .Where(o => o.Status == CustomOrderStatus.AwaitingPayment && o.UpdatedAt < cutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var id in candidateIds)
        {
            var paymentState = await db.CustomOrders
                .AsNoTracking()
                .Where(o => o.Id == id && o.Status == CustomOrderStatus.AwaitingPayment)
                .Select(o => new { o.BogOrderId, o.BogOrderKey })
                .FirstOrDefaultAsync(ct);

            if (paymentState is null)
                continue;

            if (!string.IsNullOrWhiteSpace(paymentState.BogOrderId))
            {
                var verification = await payment.VerifyCallbackAsync(paymentState.BogOrderId);
                if (verification.IsApproved)
                {
                    await customOrders.HandlePaymentCallbackAsync(paymentState.BogOrderId, paymentState.BogOrderKey);
                    continue;
                }

                if (BogPaymentStatus.IsPending(verification.Status))
                {
                    _logger.LogInformation(
                        "Skipping abandoned cancellation for custom order {OrderId}; BOG status is still {Status} (BOG: {BogOrderId})",
                        id, verification.Status, paymentState.BogOrderId);
                    continue;
                }
            }

            var claimed = await db.CustomOrders
                .Where(o => o.Id == id && o.Status == CustomOrderStatus.AwaitingPayment)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.Status, CustomOrderStatus.Cancelled)
                          .SetProperty(o => o.UpdatedAt, DateTime.UtcNow),
                    ct);

            if (claimed == 0)
                continue;

            db.CustomOrderStatusLogs.Add(new CustomOrderStatusLog
            {
                CustomOrderId = id,
                FromStatus = CustomOrderStatus.AwaitingPayment,
                ToStatus = CustomOrderStatus.Cancelled,
                Notes = $"Abandoned — payment not confirmed within {_abandonmentTimeout.TotalMinutes:F0} minutes",
            });

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Abandoned custom order {OrderId} cancelled", id);
        }
    }

}
