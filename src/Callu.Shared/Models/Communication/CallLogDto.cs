namespace Callu.Shared.Models.Communication;

/// <summary>
/// DTO for displaying call log entries in the UI
/// </summary>
public class CallLogDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CalledPersonName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int AttemptNumber { get; set; }
    public string? FailureReason { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string FormattedDuration => DurationSeconds > 0 
        ? $"{DurationSeconds / 60}:{DurationSeconds % 60:D2}" 
        : "—";
}
