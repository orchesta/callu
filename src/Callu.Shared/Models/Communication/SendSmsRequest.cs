namespace Callu.Shared.Models.Communication;

/// <summary>
/// Request to send an SMS
/// </summary>
public class SendSmsRequest
{
    public string To { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SenderId { get; set; }
}
