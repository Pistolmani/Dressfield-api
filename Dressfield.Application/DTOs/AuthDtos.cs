namespace Dressfield.Application.DTOs;

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string ConfirmPassword,
    string? Phone);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string AccessToken, UserDto User)
{
    public string? RefreshToken { get; init; }
}

public record UserDto(string Id, string Email, string FirstName, string LastName, string Role);

public record ForgotPasswordRequest(string Email);

public record GoogleLoginRequest(string IdToken);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
