namespace Dressfield.Core.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file stream to blob storage and returns the stored URL.
    /// </summary>
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);

    /// <summary>
    /// Deletes a blob by its URL. Silently ignores if not found.
    /// </summary>
    Task DeleteAsync(string fileUrl);

    /// <summary>
    /// Returns a short-lived, read-only URL for the stored blob.
    /// Azure: generates a SAS-signed URL with TTL from configuration.
    /// Local-fallback: returns the URL unchanged.
    /// </summary>
    string GetSignedReadUrl(string storedUrl);
}
