namespace Callu.Shared.Models.Settings;

/// <summary>
/// Request to update SMTP settings
/// </summary>
public record UpdateSmtpSettingsRequest
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "CalluApp";
    public string? ReplyToAddress { get; init; }
}
