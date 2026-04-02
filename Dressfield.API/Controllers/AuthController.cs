using System.Security.Claims;
using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;

    public AuthController(IAuthService authService, IConfiguration config)
    {
        _authService = authService;
        _config      = config;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);
        SetRefreshTokenCookie(response.RefreshToken!);
        return Created("", new { response.AccessToken, response.User });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        SetRefreshTokenCookie(response.RefreshToken!);
        return Ok(new { response.AccessToken, response.User });
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized();

        var response = await _authService.RefreshTokenAsync(refreshToken);
        SetRefreshTokenCookie(response.RefreshToken!);
        return Ok(new { response.AccessToken, response.User });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
            await _authService.LogoutAsync(userId);

        Response.Cookies.Delete("refreshToken");
        return Ok();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        // Use the configured frontend URL — never trust the Origin/Host header from the request
        var frontendBase = _config["App:FrontendBaseUrl"]
            ?? throw new InvalidOperationException("App:FrontendBaseUrl is not configured.");
        var resetBaseUrl = $"{frontendBase.TrimEnd('/')}/auth/reset-password";

        await _authService.ForgotPasswordAsync(request.Email, resetBaseUrl);
        return Ok(); // Always return OK — never reveal whether the email exists
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await _authService.ResetPasswordAsync(request);
        return Ok();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        var user = await _authService.GetCurrentUserAsync(userId);
        return user != null ? Ok(user) : NotFound();
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
        {
            HttpOnly  = true,
            Secure    = true,
            SameSite  = SameSiteMode.None,
            Expires   = DateTimeOffset.UtcNow.AddDays(7),
            Path      = "/api/auth"
        });
    }
}
