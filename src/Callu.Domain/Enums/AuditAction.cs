namespace Callu.Domain.Enums;

/// <summary>
/// Types of audit actions
/// </summary>
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Viewed = 4,
    Login = 5,
    Logout = 6,
    PasswordChanged = 7,
    RoleAssigned = 8,
    RoleRemoved = 9,
    SettingsChanged = 10
}
