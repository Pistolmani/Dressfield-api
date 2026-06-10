using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Dressfield.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DressfieldDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;
    private readonly byte[] _refreshTokenPepper;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        DressfieldDbContext db,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _db = db;
        _config = config;
        _logger = logger;

        // Production presence/length is enforced at startup in Program.cs; here we only
        // provide a Development fallback so local dev doesn't need the env var set.
        var pepper = config["Jwt:RefreshTokenPepper"];
        if (string.IsNullOrWhiteSpace(pepper))
            pepper = config["Jwt:Secret"] ?? "dev-refresh-token-pepper";
        _refreshTokenPepper = Encoding.UTF8.GetBytes(pepper);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.Phone
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "Customer");

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                QueueWelcomeEmail(user.Email!, user.FirstName);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue welcome email for user {UserId}", user.Id);
            }
        }

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email)
                   ?? throw new UnauthorizedAccessException("ელ-ფოსტა ან პაროლი არასწორია");

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedAccessException("ელ-ფოსტა ან პაროლი არასწორია");

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        // Atomic single-use revocation: ExecuteUpdateAsync issues a single UPDATE … WHERE,
        // so two concurrent requests racing on the same token both attempt the update but
        // only one row is affected — the loser is rejected.
        var tokenHash = ComputeRefreshTokenHash(refreshToken);
        var rowsUpdated = await _db.RefreshTokens
            .Where(r => r.Token == tokenHash && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRevoked, true));

        if (rowsUpdated == 0)
            throw new UnauthorizedAccessException("Invalid refresh token");

        var stored = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstAsync(r => r.Token == tokenHash);

        return await GenerateAuthResponse(stored.User);
    }

    public async Task LogoutAsync(string userId)
    {
        await _db.RefreshTokens
            .Where(r => r.UserId == userId && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRevoked, true));
    }

    public async Task ForgotPasswordAsync(string email, string resetBaseUrl)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return; // Don't reveal if email exists

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        // Fragment-encoded so the reset token never reaches the server in Referer headers,
        // proxy access logs, or analytics that parse the query string.
        var resetLink = $"{resetBaseUrl}#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        QueuePasswordResetEmail(email, resetLink);
        await _db.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email)
                   ?? throw new InvalidOperationException("მომხმარებელი ვერ მოიძებნა");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return ToDto(user, roles.FirstOrDefault() ?? "Customer");
    }

    public async Task<AuthResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default)
    {
        var googleClientId = _config["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(googleClientId))
            throw new InvalidOperationException("Google:ClientId is not configured.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { googleClientId }
                });
        }
        catch (InvalidJwtException)
        {
            throw new UnauthorizedAccessException("Google token is invalid or expired.");
        }

        if (!payload.EmailVerified)
            throw new UnauthorizedAccessException("Google account email is not verified.");

        var user = await _userManager.FindByLoginAsync("Google", payload.Subject);

        if (user == null)
        {
            user = await _userManager.FindByEmailAsync(payload.Email);

            if (user == null)
            {
                var nameParts = (payload.Name ?? "").Split(' ', 2);
                user = new ApplicationUser
                {
                    UserName       = payload.Email,
                    Email          = payload.Email,
                    EmailConfirmed = true,
                    FirstName      = nameParts.ElementAtOrDefault(0) ?? payload.Email,
                    LastName       = nameParts.ElementAtOrDefault(1) ?? "",
                    CreatedAt      = DateTime.UtcNow,
                };
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    throw new InvalidOperationException(
                        string.Join("; ", createResult.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(user, "Customer");
            }

            var loginInfo = new UserLoginInfo("Google", payload.Subject, "Google");
            var addResult = await _userManager.AddLoginAsync(user, loginInfo);
            if (!addResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join("; ", addResult.Errors.Select(e => e.Description)));
        }

        return await GenerateAuthResponse(user);
    }

    public async Task<UserDto> UpdateProfileAsync(string userId, UpdateProfileRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId)
                   ?? throw new InvalidOperationException("მომხმარებელი ვერ მოიძებნა");

        user.FirstName    = request.FirstName.Trim();
        user.LastName     = request.LastName.Trim();
        user.PhoneNumber  = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.AddressLine1 = string.IsNullOrWhiteSpace(request.AddressLine1) ? null : request.AddressLine1.Trim();
        user.City         = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        var roles = await _userManager.GetRolesAsync(user);
        return ToDto(user, roles.FirstOrDefault() ?? "Customer");
    }

    public async Task DeleteAccountAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
                   ?? throw new InvalidOperationException("მომხმარებელი ვერ მოიძებნა");

        // Revoke all refresh tokens first
        var tokens = await _db.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        _db.RefreshTokens.RemoveRange(tokens);
        await _db.SaveChangesAsync();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private static UserDto ToDto(ApplicationUser user, string role) =>
        new(user.Id, user.Email!, user.FirstName, user.LastName, role,
            user.PhoneNumber, user.AddressLine1, user.City);

    private async Task<AuthResponse> GenerateAuthResponse(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Customer";

        var accessToken = GenerateAccessToken(user, role);
        var refreshToken = await GenerateRefreshToken(user.Id);

        return new AuthResponse(accessToken, ToDto(user, role)) { RefreshToken = refreshToken };
    }

    private string GenerateAccessToken(ApplicationUser user, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Role, role),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(_config.GetValue("Jwt:AccessTokenExpirationMinutes", 15));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> GenerateRefreshToken(string userId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = ComputeRefreshTokenHash(rawToken),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(_config.GetValue("Jwt:RefreshTokenExpirationDays", 7))
        });

        await _db.SaveChangesAsync();
        return rawToken;
    }

    private string ComputeRefreshTokenHash(string token)
    {
        var bytes = HMACSHA256.HashData(_refreshTokenPepper, Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private void QueuePasswordResetEmail(string to, string resetLink)
    {
        var safeLink = System.Net.WebUtility.HtmlEncode(resetLink);

        var html = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto;padding:24px;">
                <h2>პაროლის აღდგენა — DressField</h2>
                <p>თქვენი პაროლის აღსადგენად გამოიყენეთ ქვემოთ მოცემული ბმული:</p>
                <p><a href="{safeLink}" style="display:inline-block;background:#000;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;">პაროლის აღდგენა</a></p>
                <p style="color:#888;font-size:13px;">ბმული მოქმედებს 1 საათის განმავლობაში.</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = "პაროლის აღდგენა — DressField",
            HtmlBody = html,
        });
    }

    private void QueueWelcomeEmail(string to, string firstName)
    {
        var safeFirstName = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(firstName) ? "there" : firstName.Trim());

        var html = $"""
            <div style="font-family:sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                <h2>Welcome to DressField</h2>
                <p>Hi {safeFirstName}, your account has been created successfully.</p>
                <p>You can now browse products, create custom embroidery, and place orders.</p>
                <hr style="border:none;border-top:1px solid #eee;margin:20px 0;" />
                <p style="color:#888;font-size:13px;">— DressField</p>
            </div>
            """;

        _db.PendingEmails.Add(new PendingEmail
        {
            ToEmail = to,
            Subject = "Welcome to DressField",
            HtmlBody = html,
        });
    }
}
