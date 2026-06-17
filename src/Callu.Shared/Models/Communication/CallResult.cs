namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of a call operation
/// </summary>
public class CallResult
{
    public bool Success { get; set; }
    public string? CallId { get; set; }
    public string? SessionUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
