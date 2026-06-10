namespace Dressfield.Core.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Creates a BOG iPay payment session for the given order.
    /// Returns the URL to redirect the customer to.
    /// </summary>
    Task<PaymentSessionResult> CreateSessionAsync(int orderId, decimal amount, string orderKey, string description);

    /// <summary>
    /// Verifies a payment callback from BOG and returns the result.
    /// </summary>
    Task<PaymentVerificationResult> VerifyCallbackAsync(string bogOrderId);

    /// <summary>
    /// Looks up a BOG payment session using the external_order_id we assigned when creating it
    /// (i.e. the order's <c>BogOrderKey</c> / <c>orderKey</c> string).
    /// Returns <c>null</c> when BOG has no record for that key (order never reached BOG).
    /// Returns a result with <see cref="PaymentVerificationResult.IsTransientFailure"/> = true
    /// when the lookup endpoint is unreachable or not configured - callers must skip cancellation
    /// and retry on the next cycle.
    /// </summary>
    Task<PaymentVerificationResult?> LookupByExternalOrderIdAsync(string externalOrderId);
}

public record PaymentSessionResult(
    bool Success,
    string? RedirectUrl,
    string? BogOrderId,
    string? ErrorMessage);

public record PaymentVerificationResult(
    bool IsApproved,
    string BogOrderId,
    string? TransactionId,
    string Status,
    decimal? VerifiedAmount = null,
    string? Currency = null,
    bool IsTransientFailure = false);
