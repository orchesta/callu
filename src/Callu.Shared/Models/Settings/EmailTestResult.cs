namespace Callu.Shared.Models.Settings;

/// <summary>
/// Result of email test operation
/// </summary>
public record EmailTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime TestedAt { get; init; } = DateTime.UtcNow;
}
