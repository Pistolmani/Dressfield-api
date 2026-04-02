namespace Dressfield.Core.Interfaces;

public interface IFileSecurityScanner
{
    Task<FileScanResult> ScanAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed record FileScanResult(bool IsClean, string? ThreatName = null);
