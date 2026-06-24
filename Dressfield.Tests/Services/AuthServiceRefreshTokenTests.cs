using System.Security.Cryptography;
using System.Text;
using Dressfield.Application.DTOs;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dressfield.Tests.Services;

public sealed class AuthServiceRefreshTokenTests : IDisposable
{
    private const string Pepper = "unit-test-pepper-at-least-32-characters-long";

    private readonly DressfieldDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _identity;
    private readonly AuthService _auth;

    public AuthServiceRefreshTokenTests()
    {
        (_db, _conn) = TestDb.Create();
        _identity = IdentityTestHost.Build(_db);
        _auth = new AuthService(
            _identity.GetRequiredService<UserManager<ApplicationUser>>(),
            _db, TestConfig.Build(), NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        _identity.Dispose();
        _db.Dispose();
        _conn.Dispose();
    }

    private async Task<(ApplicationUser User, string RefreshToken)> RegisterAndLoginAsync()
    {
        var userManager = _identity.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = _identity.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Customer"))
            await roleManager.CreateAsync(new IdentityRole("Customer"));

        var user = new ApplicationUser
        {
            UserName = "user@test.local",
            Email = "user@test.local",
            FirstName = "Test",
            LastName = "User",
        };
        var created = await userManager.CreateAsync(user, "secret123");
        Assert.True(created.Succeeded, string.Join(";", created.Errors.Select(e => e.Description)));

        var response = await _auth.LoginAsync(new LoginRequest("user@test.local", "secret123"));
        Assert.NotNull(response.RefreshToken);
        return (user, response.RefreshToken!);
    }

    [Fact]
    public async Task ValidToken_IssuesNewPair_AndRevokesOldToken()
    {
        var (_, refreshToken) = await RegisterAndLoginAsync();

        var response = await _auth.RefreshTokenAsync(refreshToken);

        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));
        Assert.NotEqual(refreshToken, response.RefreshToken);

        // The original token row is now revoked
        Assert.Equal(1, await _db.RefreshTokens.CountAsync(r => r.IsRevoked));
    }

    [Fact]
    public async Task ReplayedToken_IsRejected_AtomicSingleUse()
    {
        var (_, refreshToken) = await RegisterAndLoginAsync();

        await _auth.RefreshTokenAsync(refreshToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.RefreshTokenAsync(refreshToken));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var (user, _) = await RegisterAndLoginAsync();

        // Insert an expired token directly, hashed the same way AuthService does.
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Pepper), Encoding.UTF8.GetBytes(rawToken)));
        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = hash,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.RefreshTokenAsync(rawToken));
    }

    [Fact]
    public async Task GarbageToken_IsRejected()
    {
        await RegisterAndLoginAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.RefreshTokenAsync("not-a-real-token"));
    }
}
