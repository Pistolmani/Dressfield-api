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
    // Orders stuck in PaymentProcessing (crash during callback) are reset after a window
    // wide enough to avoid clobbering a slow but legitimate in-flight verification.
    // Real BOG verifications complete in <2s, but network retries or BOG-side latency can push
    // a healthy callback well past 5min; 15min is the conservative default.
    private readonly TimeSpan _paymentProcessingTimeout;

    public AbandonedOrderReaper(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedOrderReaper> logger,
        IConfiguration config)
    {
        _scopeFactory              = scopeFactory;
        _logger                    = logger;
        _interval                  = TimeSpan.FromMinutes(config.GetValue("Orders:ReaperIntervalMinutes", 5));
        _abandonmentTimeout        = TimeSpan.FromMinutes(config.GetValue("Orders:AbandonmentTimeoutMinutes", 30));
        _pendingTimeout            = TimeSpan.FromMinutes(config.GetValue("Orders:PendingTimeoutMinutes", 10));
        _paymentProcessingTimeout  = TimeSpan.FromMinutes(config.GetValue("Orders:PaymentProcessingTimeoutMinutes", 15));
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
        var abandonmentCutoff          = now - _abandonmentTimeout;
        var pendingCutoff              = now - _pendingTimeout;
        var paymentProcessingCutoff    = now - _paymentProcessingTimeout;

        await CancelStalePendingOrdersAsync(db, pendingCutoff, ct);
        await CancelStalePendingCustomOrdersAsync(db, pendingCutoff, ct);
        // Reset before CancelAbandoned so the same cycle can pick up and resolve the recovered orders
        await ResetStuckPaymentProcessingOrdersAsync(db, paymentProcessingCutoff, ct);
        await ResetStuckPaymentProcessingCustomOrdersAsync(db, paymentProcessingCutoff, ct);
        await CancelAbandonedOrdersAsync(db, payment, orders, abandonmentCutoff, ct);
        await CancelAbandonedCustomOrdersAsync(db, payment, customOrders, abandonmentCutoff, ct);
        await PurgeExpiredRefreshTokensAsync(db, ct);
    }

    /// <summary>
    /// Resets orders stuck in <see cref="OrderStatus.PaymentProcessing"/> back to
    /// <see cref="OrderStatus.AwaitingPayment"/> so the existing abandoned-order path can resolve them.
    /// An order enters PaymentProcessing when a callback is claimed but can get stuck there if the
    /// process crashes before the BOG verification completes (which normally takes &lt;2 seconds).
    /// Resetting without changing UpdatedAt lets CancelAbandonedOrdersAsync pick up the order in the
    /// same reaper cycle (if it has already exceeded the abandonment timeout).
    /// </summary>
    private async Task ResetStuckPaymentProcessingOrdersAsync(DressfieldDbContext db, DateTime cutoff, CancellationToken ct)
    {
        var count = await db.Orders
            .Where(o => o.Status == OrderStatus.PaymentProcessing && o.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.AwaitingPayment), ct);

        if (count > 0)
            _logger.LogWarning(
                "Reset {Count} order(s) from PaymentProcessing → AwaitingPayment (likely crash during callback verification)",
                count);
    }

    /// <summary>
    /// Same recovery for custom orders stuck in <see cref="CustomOrderStatus.PaymentProcessing"/>.
    /// </summary>
    private async Task ResetStuckPaymentProcessingCustomOrdersAsync(DressfieldDbContext db, DateTime cutoff, CancellationToken ct)
    {
        var count = await db.CustomOrders
            .Where(o => o.Status == CustomOrderStatus.PaymentProcessing && o.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, CustomOrderStatus.AwaitingPayment), ct);

        if (count > 0)
            _logger.LogWarning(
                "Reset {Count} custom order(s) from PaymentProcessing → AwaitingPayment (likely crash during callback verification)",
                count);
    }

    /// <summary>
    /// Deletes refresh tokens that are either revoked or expired.
    /// Keeps the table bounded and removes forensic noise.
    /// </summary>
    private async Task PurgeExpiredRefreshTokensAsync(DressfieldDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var deleted = await db.RefreshTokens
            .Where(r => r.IsRevoked || r.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Purged {Count} expired/revoked refresh token(s)", deleted);
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

                if (verification.IsTransientFailure)
                {
                    _logger.LogWarning(
                        "Skipping abandoned cancellation for order {OrderId}; BOG verification temporarily unreachable (BOG: {BogOrderId})",
                        id, paymentState.BogOrderId);
                    continue;
                }

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

                if (verification.IsTransientFailure)
                {
                    _logger.LogWarning(
                        "Skipping abandoned cancellation for custom order {OrderId}; BOG verification temporarily unreachable (BOG: {BogOrderId})",
                        id, paymentState.BogOrderId);
                    continue;
                }

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
