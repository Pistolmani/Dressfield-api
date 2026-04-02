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
    string Status);
