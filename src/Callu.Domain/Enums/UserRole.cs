namespace Callu.Domain.Enums;

/// <summary>
/// User roles for access control
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Full system administrator
    /// </summary>
    Admin = 0,
    
    /// <summary>
    /// Team lead with management capabilities
    /// </summary>
    TeamLead = 1,
    
    /// <summary>
    /// Regular team member
    /// </summary>
    Member = 2,
    
    /// <summary>
    /// Read-only viewer
    /// </summary>
    Viewer = 3
}
