using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Localization;
using Callu.Shared.Models.Auth;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Authentication service — stateless JWT with refresh token rotation.
/// </summary>
public class AuthService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IJwtTokenService jwtTokenService,
    IOptions<JwtSettings> jwtSettings,
    IHttpContextAccessor httpContextAccessor,
    ITransactionManager transactionManager,
    IRefreshTokenRepository refreshTokenRepository,
    IAccessTokenRevocationStore accessTokenRevocationStore,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return new AuthResponse
            {
                Success = false,
                Message = Messages.Get("auth.invalidCredentials")
            };
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login attempt for locked account: {Email}", request.Email);
            return new AuthResponse
            {
                Success = false,
                Message = Messages.Get("auth.lockedOut")
            };
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);

            logger.LogWarning(
                "Login failed for {Email}: Invalid password, IP={IP}, UA={UserAgent}",
                request.Email,
                httpContextAccessor.HttpContext?.Connection.RemoteIpAddress,
                httpContextAccessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault());

            return new AuthResponse
            {
                Success = false,
                Message = Messages.Get("auth.invalidCredentials")
            };
        }

        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            return new AuthResponse
            {
                Success = false,
                Message = Messages.Get("auth.emailNotConfirmed")
            };
        }

        await userManager.ResetAccessFailedCountAsync(user);

        logger.LogInformation(
            "User logged in: {Email}, IP={IP}, UA={UserAgent}",
            user.Email,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress,
            httpContextAccessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault());

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Member";
        var roleClaims = await GetRoleClaimsAsync(roles);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            user.LastLoginAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            var (refreshTokenPlaintext, refreshTokenEntity) = CreateRefreshToken(user.Id);
            await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

            return new AuthResponse
            {
                Success = true,
                Message = Messages.Get("auth.loginSuccess"),
                Token = jwtTokenService.GenerateAccessToken(user, roles, roleClaims),
                ExpiresAt = expiresAt,
                RefreshToken = refreshTokenPlaintext,
                User = MapUserInfo(user, role)
            };
        }, cancellationToken);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (storedToken == null)
        {
            return new AuthResponse { Success = false, Message = Messages.Get("auth.invalidRefreshToken") };
        }

        if (storedToken.IsRevoked)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}, family {FamilyId}. Revoking entire family.",
                storedToken.UserId, storedToken.FamilyId);

            await RevokeTokenFamilyAsync(storedToken.FamilyId, cancellationToken);

            return new AuthResponse { Success = false, Message = Messages.Get("auth.tokenReuseDetected") };
        }

        if (storedToken.IsExpired)
        {
            return new AuthResponse { Success = false, Message = Messages.Get("auth.refreshTokenExpired") };
        }

        var user = await userManager.FindByIdAsync(storedToken.UserId);
        if (user == null)
        {
            return new AuthResponse { Success = false, Message = Messages.Get("auth.userNotFound") };
        }

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            storedToken.RevokedAt = DateTime.UtcNow;

            var (newRefreshTokenPlaintext, newRefreshTokenEntity) = CreateRefreshToken(user.Id, storedToken.FamilyId);
            storedToken.ReplacedByTokenHash = newRefreshTokenEntity.TokenHash;

            await refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Member";
            var roleClaims = await GetRoleClaimsAsync(roles);
            var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

            logger.LogInformation("Token refreshed for user {Email}", user.Email);

            return new AuthResponse
            {
                Success = true,
                Message = Messages.Get("auth.tokenRefreshed"),
                Token = jwtTokenService.GenerateAccessToken(user, roles, roleClaims),
                ExpiresAt = expiresAt,
                RefreshToken = newRefreshTokenPlaintext,
                User = MapUserInfo(user, role)
            };
        }, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return;

        await refreshTokenRepository.RevokeAllActiveForUserAsync(userId, "logout", cancellationToken);

        var jti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (!string.IsNullOrEmpty(jti))
        {
            var expUnix = principal?.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            var ttl = TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes);
            if (long.TryParse(expUnix, out var expSeconds))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                var remaining = exp - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero) ttl = remaining;
            }

            await accessTokenRevocationStore.RevokeAsync(jti, ttl);
        }

        logger.LogInformation("Refresh tokens and access token revoked for user {UserId}", userId);
    }

    private (string Plaintext, RefreshToken Entity) CreateRefreshToken(string userId, Guid? familyId = null)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashToken(plaintext);
        var clientIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedByIp = clientIp
        };

        return (plaintext, entity);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }

    private async Task RevokeTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var familyTokens = await refreshTokenRepository.GetActiveByFamilyIdAsync(familyId, cancellationToken);

            foreach (var token in familyTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
            }
        }, cancellationToken);
    }

    private async Task<IList<Claim>> GetRoleClaimsAsync(IList<string> roles)
    {
        var claims = new List<Claim>();
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role != null)
                claims.AddRange(await roleManager.GetClaimsAsync(role));
        }
        return claims;
    }

    private static UserInfo MapUserInfo(ApplicationUser user, string role) =>
        new()
        {
            Id = user.Id,
            Name = user.DisplayName ?? user.UserName ?? "",
            Email = user.Email ?? "",
            Role = role
        };
}
