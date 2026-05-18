using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public interface IBogTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Singleton OAuth2 token cache for the Bank of Georgia iPay API.
/// Tokens are reused until they're within 30 seconds of expiry.
/// </summary>
public class BogTokenProvider : IBogTokenProvider, IDisposable
{
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BogTokenProvider> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenUrl;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public BogTokenProvider(IHttpClientFactory httpFactory, IConfiguration config, ILogger<BogTokenProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _clientId     = config["BogIPay:ClientId"]     ?? throw new InvalidOperationException("BogIPay:ClientId is not configured.");
        _clientSecret = config["BogIPay:ClientSecret"] ?? throw new InvalidOperationException("BogIPay:ClientSecret is not configured.");
        _tokenUrl     = config["BogIPay:TokenUrl"]     ?? "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token";
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt - ExpirySafetyMargin)
            return _cachedToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt - ExpirySafetyMargin)
                return _cachedToken;

            var (token, expiresInSeconds) = await FetchTokenAsync(ct);
            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string Token, int ExpiresIn)> FetchTokenAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(nameof(BogTokenProvider));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        var res = await http.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"BOG token request failed ({res.StatusCode}).");

        using var doc = JsonDocument.Parse(raw);
        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("BOG token response missing access_token.");

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
            ? seconds
            : 300;

        _logger.LogDebug("Fetched BOG token, expires in {Seconds}s", expiresIn);
        return (token, expiresIn);
    }

    public void Dispose() => _lock.Dispose();
}
