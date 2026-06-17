namespace Callu.Domain.Enums;

/// <summary>
/// Status of a service
/// </summary>
public enum ServiceStatus
{
    Operational = 1,
    DegradedPerformance = 2,
    PartialOutage = 3,
    MajorOutage = 4,
    UnderMaintenance = 5
}
