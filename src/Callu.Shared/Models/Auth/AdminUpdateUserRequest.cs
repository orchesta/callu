namespace Callu.Shared.Models.Auth;

/// <summary>
/// Admin edit of another user's identity fields. Phone number is required for voice
/// paging, so an admin can set it for users who were invited but never filled it in.
/// </summary>
public class AdminUpdateUserRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
}
