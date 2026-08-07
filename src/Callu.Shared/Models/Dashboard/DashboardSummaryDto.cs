namespace Callu.Shared.Models.Dashboard;

/// <summary>
/// Dashboard summary data — single optimized response for the home page
/// </summary>
public record DashboardSummaryDto
{
    public int TriggeredCount { get; init; }
    public int AcknowledgedCount { get; init; }
    public int ResolvedCount { get; init; }
    public int TotalIncidents { get; init; }
    public int CriticalCount { get; init; }
    public int ResolvedRate { get; init; }

    public string Mtta { get; init; } = "0m";
    public string Mttr { get; init; } = "0m";

    public Dictionary<string, int> SeverityCounts { get; init; } = new();

    public List<Incidents.IncidentListItemDto> RecentIncidents { get; init; } = new();

    public int TotalServices { get; init; }
    public List<Services.ServiceDto> Services { get; init; } = new();
}
