using Dressfield.Application.DTOs;

namespace Dressfield.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(string userId);
    Task ForgotPasswordAsync(string email, string resetBaseUrl);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task<UserDto?> GetCurrentUserAsync(string userId);
    Task<AuthResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default);
}
