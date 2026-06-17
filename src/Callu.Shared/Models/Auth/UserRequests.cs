namespace Callu.Shared.Models.Auth;

public record InviteUserRequest(string Email, string Role);
public record ChangeRoleRequest(string Role);
