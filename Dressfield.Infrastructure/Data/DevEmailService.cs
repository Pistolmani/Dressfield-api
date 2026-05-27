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

}
