using Dressfield.Core.Interfaces;

namespace Dressfield.Tests.TestInfrastructure;

/// <summary>Scripted IPaymentService: tests set the Next* results and inspect the call logs.</summary>
public sealed class FakePaymentService : IPaymentService
{
    public PaymentSessionResult NextSession { get; set; } =
        new(true, "https://bog.test/redirect", "bog-1", null);

    public PaymentVerificationResult NextVerification { get; set; } =
        new(true, "bog-1", "txn-1", "completed", 65m, "GEL");

    /// <summary>Returned by LookupByExternalOrderIdAsync; null mimics "BOG has no record".</summary>
    public PaymentVerificationResult? NextLookup { get; set; }

    public List<int> CreateSessionCalls { get; } = [];
    public List<string> VerifyCalls { get; } = [];
    public List<string> LookupCalls { get; } = [];

    public Task<PaymentSessionResult> CreateSessionAsync(int orderId, decimal amount, string orderKey, string description)
    {
        CreateSessionCalls.Add(orderId);
        return Task.FromResult(NextSession);
    }

    public Task<PaymentVerificationResult> VerifyCallbackAsync(string bogOrderId)
    {
        VerifyCalls.Add(bogOrderId);
        return Task.FromResult(NextVerification with { BogOrderId = bogOrderId });
    }

    public Task<PaymentVerificationResult?> LookupByExternalOrderIdAsync(string externalOrderId)
    {
        LookupCalls.Add(externalOrderId);
        return Task.FromResult(NextLookup);
    }
}
