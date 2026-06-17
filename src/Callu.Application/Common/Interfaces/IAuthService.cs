using Callu.Shared.Models.Auth;

namespace Callu.Application.Common.Interfaces;

/// <summary>
/// Authentication service interface
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh an expired access token using a valid refresh token.
    /// Implements token rotation — old refresh token is revoked and a new one is issued.
    /// </summary>
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logout current user — revokes all refresh tokens for the user
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
