using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dressfield.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private const string DefaultCallbackPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAu4RUyAw3+CdkS3ZNILQh
        zHI9Hemo+vKB9U2BSabppkKjzjjkf+0Sm76hSMiu/HFtYhqWOESryoCDJoqffY0Q
        1VNt25aTxbj068QNUtnxQ7KQVLA+pG0smf+EBWlS1vBEAFbIas9d8c9b9sSEkTrr
        TYQ90WIM8bGB6S/KLVoT1a7SnzabjoLc5Qf/SLDG5fu8dH8zckyeYKdRKSBJKvhx
        tcBuHV4f7qsynQT+f2UYbESX/TLHwT5qFWZDHZ0YUOUIvb8n7JujVSGZO9/+ll/g
        4ZIWhC1MlJgPObDwRkRd8NFOopgxMcMsDIZIoLbWKhHVq67hdbwpAq9K9WMmEhPn
        PwIDAQAB
        -----END PUBLIC KEY-----
        """;

    private readonly IOrderService _orders;
    private readonly ILogger<PaymentsController> _logger;
    private readonly string _callbackPublicKeyPem;

    public PaymentsController(
        IOrderService orders,
        ILogger<PaymentsController> logger,
        IConfiguration configuration)
    {
        _orders = orders;
        _logger = logger;
        _callbackPublicKeyPem = configuration["BogIPay:CallbackPublicKeyPem"] ?? DefaultCallbackPublicKeyPem;
    }

    /// <summary>
    /// POST /api/payments/callback?key={orderKey}
    /// BOG iPay calls this endpoint with a signed JSON payload when the payment status changes.
    /// Must return 200 OK for BOG to consider the callback delivered.
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery(Name = "key")] string? key)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("BOG callback received with empty body.");
            return Ok();
        }

        var signature = Request.Headers["Callback-Signature"].FirstOrDefault();
        if (!IsValidSignature(rawBody, signature))
        {
            _logger.LogWarning("BOG callback rejected because signature verification failed.");
            return Ok();
        }

        if (!TryExtractBogOrderId(rawBody, out var orderId))
        {
            _logger.LogWarning("BOG callback received without body.order_id.");
            return Ok();
        }

        _logger.LogInformation("BOG callback received for order {BogOrderId}", orderId);

        try
        {
            await _orders.HandlePaymentCallbackAsync(orderId, key);
        }
        catch (Exception ex)
        {
            // Log but swallow — BOG must receive 200 or it retries
            _logger.LogError(ex, "Error handling BOG callback for {BogOrderId}", orderId);
        }

        return Ok();
    }

    /// <summary>
    /// Legacy GET callback kept as a harmless no-op for backward compatibility with older dev/test links.
    /// Real BOG callbacks use the signed POST endpoint above.
    /// </summary>
    [HttpGet("callback")]
    public IActionResult LegacyCallback()
    {
        _logger.LogWarning("Legacy GET /api/payments/callback was called. Real BOG callbacks must use signed POST.");
        return Ok();
    }

    private bool IsValidSignature(string rawBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_callbackPublicKeyPem);

            var payloadBytes = Encoding.UTF8.GetBytes(rawBody);
            var signatureBytes = Convert.FromBase64String(signature.Trim());

            return rsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify BOG callback signature.");
            return false;
        }
    }

    private static bool TryExtractBogOrderId(string rawBody, out string orderId)
    {
        orderId = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (!root.TryGetProperty("event", out var eventProperty)
                || !string.Equals(eventProperty.GetString(), "order_payment", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!root.TryGetProperty("body", out var body)
                || !body.TryGetProperty("order_id", out var orderIdProperty))
            {
                return false;
            }

            var parsedOrderId = orderIdProperty.GetString();
            if (string.IsNullOrWhiteSpace(parsedOrderId))
                return false;

            orderId = parsedOrderId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
