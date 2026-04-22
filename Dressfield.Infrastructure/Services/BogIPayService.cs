using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly HttpClient _http;
    private readonly ILogger<BogIPayService> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _apiBaseUrl;      // Backend URL — where BOG sends the webhook
    private readonly string _frontendBaseUrl; // Frontend URL — where the customer is redirected
    private readonly string _tokenUrl;
    private readonly string _ordersUrl;

    public BogIPayService(HttpClient http, IConfiguration config, ILogger<BogIPayService> logger)
    {
        _http = http;
        _logger = logger;
        _clientId      = config["BogIPay:ClientId"]      ?? throw new InvalidOperationException("BogIPay:ClientId is not configured.");
        _clientSecret  = config["BogIPay:ClientSecret"]  ?? throw new InvalidOperationException("BogIPay:ClientSecret is not configured.");
        _apiBaseUrl    = config["BogIPay:ApiBaseUrl"]    ?? throw new InvalidOperationException("BogIPay:ApiBaseUrl is not configured.");
        _frontendBaseUrl = config["BogIPay:FrontendBaseUrl"] ?? throw new InvalidOperationException("BogIPay:FrontendBaseUrl is not configured.");
        _tokenUrl  = config["BogIPay:TokenUrl"]  ?? "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token";
        _ordersUrl = config["BogIPay:OrdersUrl"] ?? "https://api.bog.ge/payments/v1/ecommerce/orders";
    }

    public async Task<PaymentSessionResult> CreateSessionAsync(
        int orderId, decimal amount, string orderKey, string description)
    {
        try
        {
            var token = await GetAccessTokenAsync();

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
                    total_amount = amount,
                    basket = new[]
                    {
                        new
                        {
                            quantity   = 1,
                            unit_price = amount,
                            product_id = $"ORDER-{orderId}"
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _ordersUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

            _logger.LogInformation("BOG create-order response: {Body}", raw);

            var bogOrderId  = root.GetProperty("id").GetString();
            var redirectUrl = root
                .GetProperty("_links")
                .GetProperty("redirect")
                .GetProperty("href")
                .GetString();

            _logger.LogInformation("BOG redirect URL: {Url}", redirectUrl);

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
            var token = await GetAccessTokenAsync();

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_ordersUrl}/{bogOrderId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("BOG verify-order failed {Status}: {Body}", res.StatusCode, raw);
                return new PaymentVerificationResult(false, bogOrderId, null, "error");
            }

            using var doc = JsonDocument.Parse(raw);
            var root   = doc.RootElement;
            var status = root.GetProperty("status").GetString() ?? "unknown";
            var txnId  = root.TryGetProperty("payment_detail", out var detail)
                ? detail.TryGetProperty("transaction_id", out var t) ? t.GetString() : null
                : null;

            var approved = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
            return new PaymentVerificationResult(approved, bogOrderId, txnId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying BOG payment {BogOrderId}", bogOrderId);
            return new PaymentVerificationResult(false, bogOrderId, null, "exception");
        }
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        var res = await _http.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"BOG token request failed ({res.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("BOG token response missing access_token.");
    }
}
