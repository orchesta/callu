namespace Callu.Domain.Enums;

/// <summary>
/// Criticality level of a service dependency
/// </summary>
public enum DependencyCriticality
{
    /// <summary>
    /// Critical - Service cannot function without this dependency
    /// </summary>
    Critical = 1,
    
    /// <summary>
    /// High - Major impact if dependency is down
    /// </summary>
    High = 2,
    
    /// <summary>
    /// Medium - Some features affected
    /// </summary>
    Medium = 3,
    
    /// <summary>
    /// Low - Minor impact, service can operate degraded
    /// </summary>
    Low = 4,
    
    /// <summary>
    /// Optional - Nice to have, no real impact
    /// </summary>
    Optional = 5
}
