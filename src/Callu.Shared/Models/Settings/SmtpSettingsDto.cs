namespace Callu.Shared.Models.Settings;

/// <summary>
/// SMTP settings DTO (password masked for security)
/// </summary>
public record SmtpSettingsDto
{
    public Guid Id { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? Username { get; init; }
    public bool HasPassword { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "CalluApp";
    public string? ReplyToAddress { get; init; }
    public bool IsConfigured { get; init; }
    public DateTime? LastTestedAt { get; init; }
    public string? LastTestResult { get; init; }
}
