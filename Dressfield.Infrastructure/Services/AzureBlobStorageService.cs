using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Dressfield.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Dressfield.Infrastructure.Services;

public class AzureBlobStorageService : IStorageService
{
    private readonly BlobContainerClient _container;
    private readonly string? _publicBaseUrl;
    private readonly Uri? _publicBaseUri;

    public AzureBlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["AzureStorage:ConnectionString"]
            ?? throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");

        var containerName = configuration["AzureStorage:ContainerName"] ?? "designs";

        _publicBaseUrl = configuration["AzureStorage:PublicBaseUrl"]?.TrimEnd('/');
        _publicBaseUri = Uri.TryCreate(_publicBaseUrl, UriKind.Absolute, out var parsedPublicBaseUri)
            ? parsedPublicBaseUri
            : null;

        _container = new BlobContainerClient(connectionString, containerName);
        _container.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType)
    {
        var blobName = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";
        var blobClient = _container.GetBlobClient(blobName);

        await blobClient.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = contentType });

        // Use CDN base URL if configured, otherwise use the native blob URL
        if (!string.IsNullOrEmpty(_publicBaseUrl))
            return $"{_publicBaseUrl}/{_container.Name}/{blobName}";

        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string fileUrl)
    {
        try
        {
            var blobName = TryExtractBlobName(fileUrl);
            if (string.IsNullOrWhiteSpace(blobName))
                return;

            var blobClient = _container.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }
        catch
        {
            // Silently ignore; deletion is best-effort
        }
    }

    private string? TryExtractBlobName(string fileUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var fileUri))
            return null;

        var trimmedPath = fileUri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
            return null;

        var nativeContainerPrefix = $"{_container.Name}/";
        if (fileUri.Host.Equals(_container.Uri.Host, StringComparison.OrdinalIgnoreCase)
            && trimmedPath.StartsWith(nativeContainerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(trimmedPath[nativeContainerPrefix.Length..]);
        }

        if (_publicBaseUri is null || !fileUri.Host.Equals(_publicBaseUri.Host, StringComparison.OrdinalIgnoreCase))
            return null;

        var publicBasePath = _publicBaseUri.AbsolutePath.Trim('/');
        var expectedPrefix = string.IsNullOrWhiteSpace(publicBasePath)
            ? nativeContainerPrefix
            : $"{publicBasePath}/{_container.Name}/";

        if (!trimmedPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return Uri.UnescapeDataString(trimmedPath[expectedPrefix.Length..]);
    }
}
