namespace Dressfield.Core.Entities;

public class PendingEmail
{
    public int Id { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;

    public PendingEmailStatus Status { get; set; } = PendingEmailStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
}

public enum PendingEmailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,     // Exhausted retries
}
