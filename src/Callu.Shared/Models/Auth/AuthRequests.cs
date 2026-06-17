namespace Callu.Shared.Models.Auth;

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record AcceptInvitationRequest(string Email, string Token, string NewPassword);
