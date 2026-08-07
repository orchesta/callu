namespace Callu.Domain.Enums;

/// <summary>
/// Type of service dependency
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// Service A depends on Service B (upstream)
    /// </summary>
    Upstream = 1,
    
    /// <summary>
    /// Service A is depended upon by Service B (downstream)
    /// </summary>
    Downstream = 2,
    
    /// <summary>
    /// Services depend on each other (bidirectional)
    /// </summary>
    Bidirectional = 3
}
