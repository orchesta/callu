using System.Security.Claims;

namespace Callu.Infrastructure.Identity;

/// <summary>
/// Service for generating and validating JWT tokens
/// </summary>
public interface IJwtTokenService
{
    string GenerateAccessToken(
        ApplicationUser user,
        IList<string> roles,
        IList<Claim> roleClaims);

    ClaimsPrincipal? ValidateToken(string token);
}
