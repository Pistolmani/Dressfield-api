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

            _logger.LogInformation("Email sent to {To} subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} subject={Subject}", to, subject);
            throw;
        }
    }

    public Task SendPasswordResetEmailAsync(string to, string resetLink)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto;padding:24px;">
                <h2>პაროლის აღდგენა — DressField</h2>
                <p>თქვენი პაროლის აღსადგენად გამოიყენეთ ქვემოთ მოცემული ბმული:</p>
                <p><a href="{resetLink}" style="display:inline-block;background:#000;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;">პაროლის აღდგენა</a></p>
                <p style="color:#888;font-size:13px;">ბმული მოქმედებს 1 საათის განმავლობაში.</p>
            </div>
            """;
        return SendEmailAsync(to, "პაროლის აღდგენა — DressField", html);
    }

    public Task SendOrderConfirmationAsync(string to, int orderId, string itemsHtml, string total)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                <h2 style="margin-bottom:4px;">შეკვეთა წარმატებით გაფორმდა!</h2>
                <p style="color:#888;margin-top:0;">შეკვეთის ნომერი: <strong>#{orderId}</strong></p>
                <table style="width:100%;border-collapse:collapse;margin:16px 0;">
                    <thead>
                        <tr style="border-bottom:1px solid #eee;text-align:left;font-size:13px;color:#888;">
                            <th style="padding:8px 0;">პროდუქტი</th>
                            <th style="padding:8px 0;">რ-ბა</th>
                            <th style="padding:8px 0;text-align:right;">ფასი</th>
                        </tr>
                    </thead>
                    <tbody>
                        {itemsHtml}
                    </tbody>
                </table>
                <p style="font-size:16px;font-weight:600;text-align:right;">სულ: {total}</p>
                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;">გმადლობთ შეკვეთისთვის! ჩვენი გუნდი დაგიკავშირდებათ მალე.</p>
                <p style="color:#888;font-size:13px;">— DressField</p>
            </div>
            """;
        return SendEmailAsync(to, $"შეკვეთა #{orderId} — DressField", html);
    }

    public Task SendShippingNotificationAsync(string to, int orderId)
    {
        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                <h2>თქვენი შეკვეთა გაიგზავნა!</h2>
                <p>შეკვეთის ნომერი: <strong>#{orderId}</strong></p>
                <p>თქვენი შეკვეთა გაგზავნილია და მალე მიიღებთ.</p>
                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;">— DressField</p>
            </div>
            """;
        return SendEmailAsync(to, $"შეკვეთა #{orderId} გაიგზავნა — DressField", html);
    }
}
