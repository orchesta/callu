using Asp.Versioning;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Shared.Models.Auth;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthService authService,
    IUserManagementService userManagementService) : ControllerBase
{
    /// <summary>
    /// Login with email and password — returns JWT access token + refresh token
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);

        if (!result.Success)
            return Unauthorized(ApiResponse.Fail(result.Message ?? "Invalid credentials"));

        SetRefreshTokenCookie(result.RefreshToken!);

        return Ok(ApiResponse.Ok(new LoginResponse
        {
            AccessToken = result.Token!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = result.User!
        }, result.Message));
    }

    /// <summary>
    /// Refresh an expired access token using a valid refresh token.
    /// Returns new access token + rotated refresh token.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies["calluapp_refresh"];

        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse.Fail("No refresh token"));

        var result = await authService.RefreshTokenAsync(refreshToken, ct);

        if (!result.Success)
        {
            Response.Cookies.Delete("calluapp_refresh", new CookieOptions { Path = "/api/v1/auth" });
            return Unauthorized(ApiResponse.Fail(result.Message ?? "Invalid refresh token"));
        }

        SetRefreshTokenCookie(result.RefreshToken!);

        return Ok(ApiResponse.Ok(new LoginResponse
        {
            AccessToken = result.Token!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = result.User!
        }, result.Message));
    }

    /// <summary>
    /// Get current user info from JWT claims
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var name = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);

        return Ok(ApiResponse.Ok(new UserInfo
        {
            Id = userId ?? "",
            Email = email ?? "",
            Name = name ?? "",
            Role = role ?? "Member"
        }));
    }

    /// <summary>
    /// Logout — revokes all refresh tokens for the current user
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await authService.LogoutAsync(ct);
        Response.Cookies.Delete("calluapp_refresh", new CookieOptions { Path = "/api/v1/auth" });
        return Ok(ApiResponse.Ok<object?>(null, "Logged out successfully"));
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await userManagementService.SendPasswordResetEmailAsync(request.Email, ct);
        return Ok(ApiResponse.Ok<object?>(null, "If the email exists, a reset link will be sent."));
    }

    /// <summary>
    /// Reset password using token
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var (success, error) = await userManagementService.ResetPasswordAsync(
            request.Email, request.Token, request.NewPassword, ct);

        if (!success)
            return BadRequest(ApiResponse.Fail(error ?? "Reset failed"));

        return Ok(ApiResponse.Ok<object?>(null, "Password reset successful"));
    }

    /// <summary>
    /// Accept invitation and set password (invite-only registration)
    /// </summary>
    [HttpPost("accept-invitation")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        var (success, error) = await userManagementService.AcceptInvitationAsync(
            request.Email, request.Token, request.NewPassword, ct);

        if (!success)
            return BadRequest(ApiResponse.Fail(error ?? "Invalid or expired invitation"));

        return Ok(ApiResponse.Ok<object?>(null, "Account activated. You can now log in."));
    }

    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append("calluapp_refresh", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/v1/auth",
        });
    }

}
