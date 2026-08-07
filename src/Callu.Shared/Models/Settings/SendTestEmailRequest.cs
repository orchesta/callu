namespace Callu.Shared.Models.Settings;

/// <summary>
/// Request to send a test email
/// </summary>
public record SendTestEmailRequest
{
    public string RecipientEmail { get; init; } = string.Empty;
}
