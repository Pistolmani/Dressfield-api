using Dressfield.Core.Interfaces;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Dressfield.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _config["Smtp:FromName"] ?? "DressField",
            _config["Smtp:FromEmail"] ?? "noreply@dressfield.ge"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();

        try
        {
            var host = _config["Smtp:Host"] ?? "smtp.hostinger.com";
            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 465;
            var useSsl = !string.Equals(_config["Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase);

            await client.ConnectAsync(host, port, useSsl);

            var username = _config["Smtp:Username"];
            var password = _config["Smtp:Password"];
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                await client.AuthenticateAsync(username, password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {To} subject={Subject}", LogSanitizer.MaskEmail(to), subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} subject={Subject}", LogSanitizer.MaskEmail(to), subject);
            throw;
        }
    }

}
