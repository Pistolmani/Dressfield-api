using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dressfield.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Bank of Georgia iPay integration via their REST API.
/// Docs: https://api.bog.ge/docs/payments/introduction
/// </summary>
public class BogIPayService : IPaymentService
{
    private const string Currency = "GEL";

    private readonly HttpClient _http;
    private readonly ILogger<BogIPayService> _logger;
    private readonly IBogTokenProvider _tokenProvider;
    private readonly string _apiBaseUrl;          // Backend URL — where BOG sends the webhook
    private readonly string _frontendBaseUrl;     // Frontend URL — where the customer is redirected
    private readonly string _ordersUrl;
    private readonly string _receiptUrl;
    private readonly string? _externalOrderLookupUrl; // GET by external_order_id — configure once endpoint is confirmed

    public BogIPayService(
        HttpClient http,
        IConfiguration config,
        IBogTokenProvider tokenProvider,
        ILogger<BogIPayService> logger)
    {
        _http = http;
        _logger = logger;
        _tokenProvider = tokenProvider;
        _apiBaseUrl    = config["BogIPay:ApiBaseUrl"]    ?? throw new InvalidOperationException("BogIPay:ApiBaseUrl is not configured.");
        _frontendBaseUrl = config["BogIPay:FrontendBaseUrl"] ?? throw new InvalidOperationException("BogIPay:FrontendBaseUrl is not configured.");
        _ordersUrl  = config["BogIPay:OrdersUrl"]  ?? "https://api.bog.ge/payments/v1/ecommerce/orders";
        _receiptUrl = config["BogIPay:ReceiptUrl"] ?? "https://api.bog.ge/payments/v1/receipt";
        // Optional — verify BOG's external-order-id lookup endpoint before enabling.
        // Set BogIPay:ExternalOrderLookupUrl in Azure config once confirmed.
        _externalOrderLookupUrl = config["BogIPay:ExternalOrderLookupUrl"];
    }

    public async Task<PaymentSessionResult> CreateSessionAsync(
        int orderId, decimal amount, string orderKey, string description)
    {
        try
        {
            var token = await _tokenProvider.GetAccessTokenAsync();

            var body = new
            {
                callback_url      = $"{_apiBaseUrl.TrimEnd('/')}/api/payments/callback?key={orderKey}",
                external_order_id = orderKey,
                redirect_urls = new
                {
                    success = $"{_frontendBaseUrl.TrimEnd('/')}/order-confirmation?orderId={orderId}&key={orderKey}",
                    fail    = $"{_frontendBaseUrl.TrimEnd('/')}/order-failed?orderId={orderId}"
                },
                purchase_units = new
                {
                    currency = Currency,
                    total_amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                    basket = new[]
                    {
                        new
                        {
                            quantity   = 1,
                            unit_price = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                            product_id = $"ORDER-{orderId}",
                            description = description
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _ordersUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (TryBuildBogIdempotencyKey(orderKey, out var bogIdempotencyKey))
                req.Headers.TryAddWithoutValidation("Idempotency-Key", bogIdempotencyKey);
            req.Content = JsonContent.Create(body);

            var res = await _http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("BOG create-order failed {Status}: {Body}", res.StatusCode, raw);
                return new PaymentSessionResult(false, null, null, $"BOG returned {res.StatusCode}");
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var bogOrderId  = root.GetProperty("id").GetString();
            var redirectUrl = root
                .GetProperty("_links")
                .GetProperty("redirect")
                .GetProperty("href")
                .GetString();

            _logger.LogInformation("BOG order created for order {OrderId} (BOG: {BogOrderId})", orderId, bogOrderId);

            return new PaymentSessionResult(true, redirectUrl, bogOrderId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating BOG payment session for order {OrderId}", orderId);
            return new PaymentSessionResult(false, null, null, ex.Message);
        }
    }

    public async Task<PaymentVerificationResult> VerifyCallbackAsync(string bogOrderId)
    {
        try
        {
            var token = await _tokenProvider.GetAccessTokenAsync();

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_receiptUrl.TrimEnd('/')}/{bogOrderId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("BOG verify-order failed {Status} for {BogOrderId}", res.StatusCode, bogOrderId);
                return new PaymentVerificationResult(false, bogOrderId, null, "error", IsTransientFailure: true);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var statusKey = root.TryGetProperty("order_status", out var orderStatus)
                            && orderStatus.TryGetProperty("key", out var keyEl)
                ? keyEl.GetString() ?? "unknown"
                : "unknown";

            var txnId = root.TryGetProperty("payment_detail", out var detail)
                        && detail.TryGetProperty("transaction_id", out var t)
                ? t.GetString()
                : null;

            decimal? verifiedAmount = null;
            string? verifiedCurrency = null;
            if (root.TryGetProperty("purchase_units", out var pu))
            {
                if (pu.TryGetProperty("transfer_amount", out var ta) && ta.TryGetDecimal(out var amt))
                    verifiedAmount = amt;
                if (pu.TryGetProperty("currency", out var ccy))
                    verifiedCurrency = ccy.GetString();
            }

            var approved = string.Equals(statusKey, "completed", StringComparison.OrdinalIgnoreCase);
            return new PaymentVerificationResult(approved, bogOrderId, txnId, statusKey, verifiedAmount, verifiedCurrency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying BOG payment {BogOrderId}", bogOrderId);
            return new PaymentVerificationResult(false, bogOrderId, null, "exception", IsTransientFailure: true);
        }
    }

    /// <inheritdoc />
    public async Task<PaymentVerificationResult?> LookupByExternalOrderIdAsync(string externalOrderId)
    {
        if (string.IsNullOrWhiteSpace(_externalOrderLookupUrl))
        {
            // BOG does not currently expose a lookup-by-external_order_id endpoint.
            // Return null so the reaper proceeds with cancellation.
            // Recovery for the rare retry-exhaustion case relies on the CRITICAL log written
            // by SaveBogSessionWithRetryAsync, which contains the BogOrderId for manual reconciliation.
            _logger.LogDebug(
                "LookupByExternalOrderIdAsync: BogIPay:ExternalOrderLookupUrl not configured — returning null for {ExternalOrderId}.",
                externalOrderId);
            return null;
        }

        try
        {
            var token = await _tokenProvider.GetAccessTokenAsync();
            var url = $"{_externalOrderLookupUrl.TrimEnd('/')}/{Uri.EscapeDataString(externalOrderId)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _http.SendAsync(req);

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("BOG has no record for external_order_id={ExternalOrderId}", externalOrderId);
                return null;
            }

            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("BOG external-order lookup failed {Status} for {ExternalOrderId}", res.StatusCode, externalOrderId);
                return new PaymentVerificationResult(false, string.Empty, null, "error", IsTransientFailure: true);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Parse the same shape as VerifyCallbackAsync — adjust if BOG's external-lookup response differs.
            var bogOrderId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;

            var statusKey = root.TryGetProperty("order_status", out var orderStatus)
                            && orderStatus.TryGetProperty("key", out var keyEl)
                ? keyEl.GetString() ?? "unknown"
                : "unknown";

            var txnId = root.TryGetProperty("payment_detail", out var detail)
                        && detail.TryGetProperty("transaction_id", out var t)
                ? t.GetString()
                : null;

            decimal? verifiedAmount = null;
            string? verifiedCurrency = null;
            if (root.TryGetProperty("purchase_units", out var pu))
            {
                if (pu.TryGetProperty("transfer_amount", out var ta) && ta.TryGetDecimal(out var amt))
                    verifiedAmount = amt;
                if (pu.TryGetProperty("currency", out var ccy))
                    verifiedCurrency = ccy.GetString();
            }

            var approved = string.Equals(statusKey, "completed", StringComparison.OrdinalIgnoreCase);
            return new PaymentVerificationResult(approved, bogOrderId, txnId, statusKey, verifiedAmount, verifiedCurrency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error looking up BOG external order {ExternalOrderId}", externalOrderId);
            return new PaymentVerificationResult(false, string.Empty, null, "exception", IsTransientFailure: true);
        }
    }

    private static bool TryBuildBogIdempotencyKey(string orderKey, out string idempotencyKey)
    {
        var normalized = orderKey.StartsWith("c-", StringComparison.Ordinal)
            ? orderKey[2..]
            : orderKey;

        if (Guid.TryParseExact(normalized, "N", out var guid))
        {
            idempotencyKey = guid.ToString();
            return true;
        }

        idempotencyKey = string.Empty;
        return false;
    }
}
