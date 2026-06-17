namespace Callu.Shared.Models.Teams;

public record AddMemberRequest(string UserId, string Role);
public record UpdateMemberRoleRequest(string Role);
