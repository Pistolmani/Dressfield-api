using System.Net.Sockets;
using System.Text;
using Dressfield.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Optional ClamAV scanner using clamd INSTREAM protocol.
/// Enable with Security:ClamAv:Enabled=true and provide host/port.
/// </summary>
public class ClamAvFileSecurityScanner : IFileSecurityScanner
{
    private const int DefaultPort = 3310;
    private const int DefaultTimeoutSeconds = 15;
    private const int ChunkSize = 8192;

    private readonly ILogger<ClamAvFileSecurityScanner> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutSeconds;

    public ClamAvFileSecurityScanner(
        IConfiguration configuration,
        ILogger<ClamAvFileSecurityScanner> logger)
    {
        _logger = logger;
        _host = configuration["Security:ClamAv:Host"]
            ?? throw new InvalidOperationException("Security:ClamAv:Host is required when ClamAV scanning is enabled.");
        _port = TryParseInt(configuration["Security:ClamAv:Port"], DefaultPort);
        _timeoutSeconds = TryParseInt(configuration["Security:ClamAv:TimeoutSeconds"], DefaultTimeoutSeconds);
    }

    public async Task<FileScanResult> ScanAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            await tcpClient.ConnectAsync(_host, _port, timeoutCts.Token);
            await using var networkStream = tcpClient.GetStream();

            var command = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await networkStream.WriteAsync(command, timeoutCts.Token);

            var buffer = new byte[ChunkSize];
            while (true)
            {
                var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
                if (bytesRead == 0)
                    break;

                var lengthPrefix = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytesRead));
                await networkStream.WriteAsync(lengthPrefix, timeoutCts.Token);
                await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), timeoutCts.Token);
            }

            // End stream marker (0-length chunk)
            await networkStream.WriteAsync(new byte[] { 0, 0, 0, 0 }, timeoutCts.Token);
            await networkStream.FlushAsync(timeoutCts.Token);

            var response = await ReadResponseAsync(networkStream, timeoutCts.Token);
            if (response.Contains("OK", StringComparison.OrdinalIgnoreCase))
                return new FileScanResult(true);

            if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
            {
                var threatName = response.Replace("FOUND", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                _logger.LogWarning("ClamAV flagged upload {FileName} ({ContentType}): {Threat}", fileName, contentType, threatName);
                return new FileScanResult(false, threatName);
            }

            _logger.LogWarning("Unexpected ClamAV response for {FileName}: {Response}", fileName, response);
            return new FileScanResult(false, "Unknown scan result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClamAV scan failed for {FileName}", fileName);
            return new FileScanResult(false, "Scanner unavailable");
        }
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var responseBuffer = new byte[1024];
        var responseBytes = new List<byte>(1024);

        while (true)
        {
            var count = await stream.ReadAsync(responseBuffer.AsMemory(0, responseBuffer.Length), cancellationToken);
            if (count == 0)
                break;

            for (var i = 0; i < count; i++)
            {
                var b = responseBuffer[i];
                if (b == (byte)'\n')
                    return Encoding.UTF8.GetString(responseBytes.ToArray()).Trim();
                responseBytes.Add(b);
            }
        }

        return Encoding.UTF8.GetString(responseBytes.ToArray()).Trim();
    }

    private static int TryParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
