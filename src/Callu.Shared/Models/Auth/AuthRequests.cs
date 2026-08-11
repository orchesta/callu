using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Auth;

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record AcceptInvitationRequest(string Email, string Token, string NewPassword);

/// <summary>Optional body for refresh / logout when the HttpOnly cookie is not sent (native clients).</summary>
// Bounded well above the 88-character issued token: the value is only hashed and compared, so the
// bound is there to keep an arbitrarily large body from reaching the hasher.
public record RefreshRequest([StringLength(512)] string? RefreshToken);
