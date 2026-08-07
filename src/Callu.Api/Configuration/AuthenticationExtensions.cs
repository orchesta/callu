using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Callu.Application.Common.Interfaces;
using Callu.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Callu.Api.Configuration;

/// <summary>
/// Extension methods for configuring authentication and JWT bearer.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>Substrings that mark a secret as a shipped example rather than a generated key.</summary>
    private static readonly string[] PlaceholderSecretMarkers =
    [
        "CHANGE_ME",
        "CHANGEME",
        "PLACEHOLDER",
        "YOUR_SECRET",
        "YOUR-SECRET",
        "YOURSECRET",
        "SUPERSECRET",
        "SECRET_KEY_HERE"
    ];

    private const string SecretKeyGuidance =
        "Generate a strong random key (e.g. `openssl rand -base64 48`) and set it via the " +
        "'JwtSettings:SecretKey' configuration key or the JWT_SECRET_KEY environment variable (Docker).";

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
            throw new InvalidOperationException(
                $"Configuration '{JwtSettings.SectionName}:SecretKey' is missing. {SecretKeyGuidance}");

        if (Encoding.UTF8.GetByteCount(jwtSettings.SecretKey) < 32)
            throw new InvalidOperationException(
                $"Configuration '{JwtSettings.SectionName}:SecretKey' is shorter than 32 bytes. " +
                $"HS256 requires at least 256 bits of entropy to be safe. {SecretKeyGuidance}");

        if (IsPlaceholderSecret(jwtSettings.SecretKey))
            throw new InvalidOperationException(
                $"Configuration '{JwtSettings.SectionName}:SecretKey' is still an example/placeholder value. " +
                $"{SecretKeyGuidance}");

        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) || string.IsNullOrWhiteSpace(jwtSettings.Audience))
            throw new InvalidOperationException(
                $"'{JwtSettings.SectionName}:Issuer' and ':Audience' must be configured to prevent cross-issuer token reuse.");

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var path = context.HttpContext.Request.Path;
                    if (!path.StartsWithSegments("/hubs"))
                        return Task.CompletedTask;

                    if (!string.IsNullOrEmpty(context.Request.Headers.Authorization.ToString()))
                        return Task.CompletedTask;

                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                    if (!string.IsNullOrEmpty(jti))
                    {
                        var store = context.HttpContext.RequestServices
                            .GetRequiredService<IAccessTokenRevocationStore>();

                        if (await store.IsRevokedAsync(jti, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Access token has been revoked.");
                            return;
                        }
                    }

                    var sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                              ?? context.Principal?.FindFirst("sub")?.Value
                              ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(sub))
                        return;

                    var userManager = context.HttpContext.RequestServices
                        .GetRequiredService<UserManager<ApplicationUser>>();
                    var user = await userManager.FindByIdAsync(sub);
                    if (user is null || user.IsDeleted)
                    {
                        context.Fail("User is disabled.");
                        return;
                    }

                    var stampInToken = context.Principal?.FindFirst("sst")?.Value;
                    if (!string.IsNullOrEmpty(stampInToken) &&
                        !string.Equals(stampInToken, user.SecurityStamp, StringComparison.Ordinal))
                    {
                        context.Fail("Security stamp changed; please log in again.");
                    }
                }
            };
        });

        return services;
    }

    private static bool IsPlaceholderSecret(string secretKey) =>
        PlaceholderSecretMarkers.Any(marker =>
            secretKey.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
