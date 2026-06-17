namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Result of validating rotation coverage for a schedule
/// </summary>
public record RotationCoverageResult
{
    /// <summary>
    /// Whether the schedule has full coverage (no gaps in the checked period)
    /// </summary>
    public bool HasFullCoverage { get; init; }
    
    /// <summary>
    /// Total hours of coverage gaps found
    /// </summary>
    public double GapHours { get; init; }
    
    /// <summary>
    /// Percentage of time covered (0-100)
    /// </summary>
    public double CoveragePercent { get; init; }
    
    /// <summary>
    /// Individual coverage gaps found
    /// </summary>
    public IReadOnlyList<CoverageGap> Gaps { get; init; } = Array.Empty<CoverageGap>();
}
