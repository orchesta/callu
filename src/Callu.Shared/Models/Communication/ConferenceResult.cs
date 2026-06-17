namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of a conference operation
/// </summary>
public class ConferenceResult
{
    public bool Success { get; set; }
    public string? ConferenceId { get; set; }
    public string? JoinUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
