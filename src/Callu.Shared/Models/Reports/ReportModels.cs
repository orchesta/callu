namespace Callu.Shared.Models.Reports;

/// <summary>
/// A single data point in an incident trend chart
/// </summary>
public class IncidentTrendPointDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
}

/// <summary>
/// MTTA/MTTR metrics at a point in time
/// </summary>
public class MttMetricPointDto
{
    public DateTime Date { get; set; }
    /// <summary>Mean Time to Acknowledge (minutes)</summary>
    public double MttaMinutes { get; set; }
    /// <summary>Mean Time to Resolve (minutes)</summary>
    public double MttrMinutes { get; set; }
    public int IncidentCount { get; set; }
}

/// <summary>
/// Per-service uptime percentage
/// </summary>
public class ServiceUptimeDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public double UptimePercent { get; set; }
    public int IncidentCount { get; set; }
    public double TotalDowntimeMinutes { get; set; }
}

/// <summary>
/// Per-team response performance
/// </summary>
public class TeamPerformanceDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int TotalIncidents { get; set; }
    public double AvgAcknowledgeMinutes { get; set; }
    public double AvgResolveMinutes { get; set; }
    public int ResolvedCount { get; set; }
}

/// <summary>
/// Incident count by severity level
/// </summary>
public class SeverityDistributionDto
{
    public string Severity { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}
