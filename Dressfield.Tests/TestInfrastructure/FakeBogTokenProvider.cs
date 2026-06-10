using Dressfield.Infrastructure.Services;

namespace Dressfield.Tests.TestInfrastructure;

public sealed class FakeBogTokenProvider : IBogTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken ct = default) =>
        Task.FromResult("test-bearer-token");
}
