namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Centralised knowledge of BOG iPay order status keys.
/// Update here if BOG adds or renames statuses.
/// </summary>
internal static class BogPaymentStatus
{
    /// <summary>
    /// Returns true when the BOG status is non-terminal — i.e. the payment
    /// is still in progress and the order should NOT be cancelled yet.
    /// </summary>
    public static bool IsPending(string status) =>
        status.Equals("created",       StringComparison.OrdinalIgnoreCase)
        || status.Equals("processing",    StringComparison.OrdinalIgnoreCase)
        || status.Equals("auth_requested",StringComparison.OrdinalIgnoreCase)
        || status.Equals("blocked",       StringComparison.OrdinalIgnoreCase)
        || status.Equals("error",         StringComparison.OrdinalIgnoreCase)
        || status.Equals("exception",     StringComparison.OrdinalIgnoreCase);
}
