using Dressfield.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Development fallback when BOG credentials are absent.
/// Simulates a successful payment session so checkout can be tested end-to-end.
/// </summary>
public class MockPaymentService : IPaymentService
{
    private readonly ILogger<MockPaymentService> _logger;
    private readonly string _siteBaseUrl;

    public MockPaymentService(IConfiguration config, ILogger<MockPaymentService> logger)
    {
        _logger = logger;
        _siteBaseUrl = config["BogIPay:CallbackBaseUrl"] ?? "http://localhost:3000";
    }

    public Task<PaymentSessionResult> CreateSessionAsync(
        int orderId, decimal amount, string orderKey, string description)
    {
        _logger.LogWarning(
            "[MockPayment] Simulating BOG session for order {OrderId} (₾{Amount}). Configure BogIPay:ClientId/Secret for real payments.",
            orderId, amount);

        var bogOrderId  = $"MOCK-{orderKey[..8].ToUpperInvariant()}";
        var redirectUrl = $"{_siteBaseUrl.TrimEnd('/')}/order-confirmation?orderId={orderId}&key={orderKey}&mock=1";

        return Task.FromResult(new PaymentSessionResult(true, redirectUrl, bogOrderId, null));
    }

    public Task<PaymentVerificationResult> VerifyCallbackAsync(string bogOrderId)
    {
        _logger.LogWarning("[MockPayment] Verifying mock order {BogOrderId} — always approved.", bogOrderId);
        return Task.FromResult(new PaymentVerificationResult(true, bogOrderId, $"MOCK-TXN-{Guid.NewGuid():N}", "completed"));
    }
}
