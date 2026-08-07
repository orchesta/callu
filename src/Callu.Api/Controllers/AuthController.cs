using Asp.Versioning;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Shared.Models.Auth;
using Callu.Shared.Results;
using Callu.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthService authService,
    IUserManagementService userManagementService,
    IOptions<JwtSettings> jwtSettings,
    IHostEnvironment hostEnvironment) : ControllerBase
{
    private const string RefreshCookieName = "calluapp_refresh";
    private const string RefreshCookiePath = "/api/v1/auth";
    private const string NativeClientHeader = "X-Callu-Client";
    private const string NativeClientValue = "native";
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);

        if (!result.Success)
            return Unauthorized(ApiResponse.Fail(result.Message ?? "Invalid credentials"));

        SetRefreshTokenCookie(result.RefreshToken!);

        return Ok(ApiResponse.Ok(ToLoginResponse(result), result.Message));
    }

    /// <summary>
    /// Refresh via HttpOnly cookie (SPA) or JSON body refreshToken (native). Cookie wins if both present.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth_refresh")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var refreshToken = ResolveRefreshToken(request);

        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse.Fail("No refresh token"));

        var result = await authService.RefreshTokenAsync(refreshToken, ct);

        if (!result.Success)
        {
            ClearRefreshTokenCookie();
            return Unauthorized(ApiResponse.Fail(result.Message ?? "Invalid refresh token"));
        }

        SetRefreshTokenCookie(result.RefreshToken!);

        return Ok(ApiResponse.Ok(ToLoginResponse(result), result.Message));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var name = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var stored = userId is null ? null : await userManagementService.GetUserByIdAsync(userId, ct);

        return Ok(ApiResponse.Ok(new UserInfo
        {
            Id = userId ?? "",
            Email = stored?.Email ?? email ?? "",
            Name = stored?.DisplayName ?? name ?? "",
            Role = stored?.Role ?? role ?? "Member"
        }));
    }

    /// <summary>Bearer: revoke all sessions. Body refreshToken: revoke that login family only.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
            await authService.LogoutAsync(ct);
        else if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
            await authService.LogoutByRefreshTokenAsync(request.RefreshToken, ct);

        ClearRefreshTokenCookie();
        return Ok(ApiResponse.Ok<object?>(null, "Logged out successfully"));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await userManagementService.SendPasswordResetEmailAsync(request.Email, ct);
        return Ok(ApiResponse.Ok<object?>(null, "If the email exists, a reset link will be sent."));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var (success, error) = await userManagementService.ResetPasswordAsync(
            request.Email, request.Token, request.NewPassword, ct);

        if (!success)
            return BadRequest(ApiResponse.Fail(error ?? "Reset failed"));

        return Ok(ApiResponse.Ok<object?>(null, "Password reset successful"));
    }

    [AllowAnonymous]
    [HttpPost("accept-invitation")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        var (success, error) = await userManagementService.AcceptInvitationAsync(
            request.Email, request.Token, request.NewPassword, ct);

        if (!success)
            return BadRequest(ApiResponse.Fail(error ?? "Invalid or expired invitation"));

        return Ok(ApiResponse.Ok<object?>(null, "Account activated. You can now log in."));
    }

    private string? ResolveRefreshToken(RefreshRequest? request)
    {
        var fromCookie = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrEmpty(fromCookie))
            return fromCookie;

        return string.IsNullOrWhiteSpace(request?.RefreshToken) ? null : request.RefreshToken;
    }

    private bool WantsBodyRefresh() =>
        Request.Headers.TryGetValue(NativeClientHeader, out var values)
        && values.Any(v => string.Equals(v, NativeClientValue, StringComparison.OrdinalIgnoreCase));

    private LoginResponse ToLoginResponse(AuthResponse result) => new()
    {
        AccessToken = result.Token!,
        ExpiresAt = result.ExpiresAt!.Value,
        User = result.User!,
        RefreshToken = WantsBodyRefresh() ? result.RefreshToken : null,
    };

    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !hostEnvironment.IsDevelopment() || HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            Path = RefreshCookiePath,
        });
    }

    private void ClearRefreshTokenCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
}
