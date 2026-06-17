namespace Callu.Shared.Constants;

/// <summary>
/// Centralized status values for StatusPage components.
/// Used consistently across backend and frontend.
/// </summary>
public static class ComponentStatuses
{
    public const string Operational = "operational";
    public const string Degraded = "degraded";
    public const string PartialOutage = "partial_outage";
    public const string MajorOutage = "major_outage";
    public const string Maintenance = "maintenance";

    /// <summary>
    /// All valid status values.
    /// </summary>
    public static readonly string[] All =
    [
        Operational,
        Degraded,
        PartialOutage,
        MajorOutage,
        Maintenance
    ];

    /// <summary>
    /// Validates if a given status string is a recognized value.
    /// </summary>
    public static bool IsValid(string? status) =>
        !string.IsNullOrEmpty(status) && All.Contains(status);

    /// <summary>
    /// Single source of truth for "page-level status = worst component status".
    /// Priority: MajorOutage &gt; PartialOutage &gt; Maintenance &gt; Degraded &gt; Operational.
    /// Maintenance ranks above Degraded so a scheduled window doesn't look worse than
    /// a real degradation; ranks below outages so a real outage during maintenance
    /// still surfaces.
    /// </summary>
    public static string AggregateOverallStatus(IEnumerable<string> componentStatuses)
    {
        var set = new HashSet<string>(componentStatuses);
        if (set.Contains(MajorOutage)) return MajorOutage;
        if (set.Contains(PartialOutage)) return PartialOutage;
        if (set.Contains(Maintenance)) return Maintenance;
        if (set.Contains(Degraded)) return Degraded;
        return Operational;
    }

    /// <summary>
    /// Allowed HTTP methods for health checks.
    /// </summary>
    public static readonly string[] AllowedHttpMethods = ["GET", "HEAD", "POST"];

    /// <summary>
    /// Max response body size for health checks (64KB).
    /// </summary>
    public const int MaxResponseBodyBytes = 64 * 1024;

    /// <summary>
    /// Max health check body size (10KB).
    /// </summary>
    public const int MaxHealthCheckBodyLength = 10_000;

    /// <summary>
    /// Max field/state mapping size (10KB / 5KB).
    /// </summary>
    public const int MaxFieldMappingsLength = 10_000;
    public const int MaxStateMappingLength = 5_000;
    public const int MaxSampleResponseLength = 65_536;
}
