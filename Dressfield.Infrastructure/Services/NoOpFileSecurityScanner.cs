using Dressfield.Core.Interfaces;

namespace Dressfield.Infrastructure.Services;

/// <summary>
/// Development fallback when malware scanning is not configured.
/// Always returns clean.
/// </summary>
public class NoOpFileSecurityScanner : IFileSecurityScanner
{
    public Task<FileScanResult> ScanAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileScanResult(true));
    }
}
