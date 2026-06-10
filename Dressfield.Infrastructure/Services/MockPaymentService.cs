using Dressfield.Core.Interfaces;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
    private readonly DressfieldDbContext _db;
    private readonly string _siteBaseUrl;

    public MockPaymentService(IConfiguration config, ILogger<MockPaymentService> logger, DressfieldDbContext db)
    {
        _logger = logger;
        _db = db;
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

    public Task<PaymentVerificationResult?> LookupByExternalOrderIdAsync(string externalOrderId)
    {
        // Mock has no orphaned sessions - return null (no record found, safe to cancel).
        _logger.LogWarning("[MockPayment] LookupByExternalOrderIdAsync({ExternalOrderId}) → null (mock).", externalOrderId);
        return Task.FromResult<PaymentVerificationResult?>(null);
    }

    public async Task<PaymentVerificationResult> VerifyCallbackAsync(string bogOrderId)
    {
        _logger.LogWarning("[MockPayment] Verifying mock order {BogOrderId} — always approved.", bogOrderId);

        // Look up the order's stored total so the consumer's amount-mismatch check passes in dev.
        var amount = await _db.Orders
            .Where(o => o.BogOrderId == bogOrderId)
            .Select(o => (decimal?)o.TotalAmount)
            .FirstOrDefaultAsync();

        amount ??= await _db.CustomOrders
            .Where(o => o.BogOrderId == bogOrderId)
            .Select(o => (decimal?)o.TotalPrice)
            .FirstOrDefaultAsync();

        return new PaymentVerificationResult(
            true, bogOrderId, $"MOCK-TXN-{Guid.NewGuid():N}", "completed", amount, "GEL");
    }
}
