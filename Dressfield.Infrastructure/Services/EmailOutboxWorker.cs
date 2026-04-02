using Dressfield.Core.Entities;
using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Background service that drains the PendingEmails outbox.
/// Retries up to 3 times with exponential back-off (5 min, 30 min, 2 h).
/// </summary>
public class EmailOutboxWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly int[] RetryDelayMinutes = [5, 30, 120]; // back-off per attempt
    private const int MaxRetries = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(IServiceScopeFactory scopeFactory, ILogger<EmailOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailOutboxWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "EmailOutboxWorker encountered an error during batch processing.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DressfieldDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;
        var batch = await db.PendingEmails
            .Where(e => e.Status == PendingEmailStatus.Pending && e.NextRetryAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        _logger.LogInformation("EmailOutboxWorker processing {Count} pending email(s).", batch.Count);

        foreach (var pending in batch)
        {
            try
            {
                await email.SendEmailAsync(pending.ToEmail, pending.Subject, pending.HtmlBody);
                pending.Status = PendingEmailStatus.Sent;
                pending.SentAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                pending.RetryCount++;
                pending.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

                if (pending.RetryCount >= MaxRetries)
                {
                    pending.Status = PendingEmailStatus.Failed;
                    _logger.LogError(ex, "Email {Id} to {To} permanently failed after {Retries} retries.",
                        pending.Id, pending.ToEmail, pending.RetryCount);
                }
                else
                {
                    var delayMinutes = RetryDelayMinutes[pending.RetryCount - 1];
                    pending.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
                    _logger.LogWarning(ex, "Email {Id} to {To} failed (attempt {Attempt}/{Max}), retry in {Delay} min.",
                        pending.Id, pending.ToEmail, pending.RetryCount, MaxRetries, delayMinutes);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
