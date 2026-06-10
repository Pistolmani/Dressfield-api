using Dressfield.Core.Interfaces;

namespace Dressfield.Tests.TestInfrastructure;

public sealed class FakeStorageService : IStorageService
{
    public List<string> Uploaded { get; } = [];
    public List<string> Deleted { get; } = [];

    public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType)
    {
        var url = $"https://storage.test/designs/{Guid.NewGuid():N}-{fileName}";
        Uploaded.Add(url);
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string fileUrl)
    {
        Deleted.Add(fileUrl);
        return Task.CompletedTask;
    }

    public string GetSignedReadUrl(string storedUrl) => storedUrl;
}
