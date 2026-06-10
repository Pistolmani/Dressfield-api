using Microsoft.Extensions.Configuration;

namespace Dressfield.Tests.TestInfrastructure;

public static class TestConfig
{
    /// <summary>Builds an in-memory IConfiguration with sane auth defaults; overrides win.</summary>
    public static IConfiguration Build(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "unit-test-secret-at-least-32-characters-long",
            ["Jwt:Issuer"] = "https://test.local",
            ["Jwt:Audience"] = "https://test.local",
            ["Jwt:AccessTokenExpirationMinutes"] = "15",
            ["Jwt:RefreshTokenExpirationDays"] = "7",
            ["Jwt:RefreshTokenPepper"] = "unit-test-pepper-at-least-32-characters-long",
        };
        foreach (var (key, value) in overrides)
            values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
