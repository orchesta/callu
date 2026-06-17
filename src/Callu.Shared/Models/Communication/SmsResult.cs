namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of an SMS operation
/// </summary>
public class SmsResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
