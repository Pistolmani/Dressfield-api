namespace Dressfield.Core.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
    Task SendPasswordResetEmailAsync(string to, string resetLink);
    Task SendOrderConfirmationAsync(string to, int orderId, string itemsHtml, string total);
    Task SendShippingNotificationAsync(string to, int orderId);
}
