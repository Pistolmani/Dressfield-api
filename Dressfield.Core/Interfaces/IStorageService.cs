namespace Dressfield.Core.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file stream to blob storage and returns the public URL.
    /// </summary>
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);

    /// <summary>
    /// Deletes a blob by its URL. Silently ignores if not found.
    /// </summary>
    Task DeleteAsync(string fileUrl);
}
