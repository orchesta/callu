namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Represents a gap in rotation coverage
/// </summary>
public record CoverageGap
{
    /// <summary>
    /// Start of the uncovered period
    /// </summary>
    public DateTime Start { get; init; }
    
    /// <summary>
    /// End of the uncovered period
    /// </summary>
    public DateTime End { get; init; }
    
    /// <summary>
    /// Duration of the gap
    /// </summary>
    public TimeSpan Duration => End - Start;
}
