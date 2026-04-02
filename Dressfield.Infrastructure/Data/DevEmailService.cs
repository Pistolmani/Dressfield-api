using Dressfield.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public class DevEmailService : IEmailService
{
    private readonly ILogger<DevEmailService> _logger;

    public DevEmailService(ILogger<DevEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        _logger.LogInformation("DEV EMAIL to={To} subject={Subject}", to, subject);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string to, string resetLink)
    {
        _logger.LogInformation("DEV PASSWORD RESET to={To} link={Link}", to, resetLink);
        return Task.CompletedTask;
    }

    public Task SendOrderConfirmationAsync(string to, int orderId, string itemsHtml, string total)
    {
        _logger.LogInformation("DEV ORDER CONFIRMATION to={To} orderId={OrderId} total={Total}", to, orderId, total);
        return Task.CompletedTask;
    }

    public Task SendShippingNotificationAsync(string to, int orderId)
    {
        _logger.LogInformation("DEV SHIPPING NOTIFICATION to={To} orderId={OrderId}", to, orderId);
        return Task.CompletedTask;
    }
}
