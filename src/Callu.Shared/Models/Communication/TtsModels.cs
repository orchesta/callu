namespace Callu.Shared.Models.Communication;

/// <summary>
/// TTS message template DTOs — response and save request
/// </summary>

public class TtsTemplateDto
{
    public Guid Id { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public Dictionary<string, string> Messages { get; set; } = new();
}

public class TtsTemplateSaveRequest
{
    public string LanguageCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public Dictionary<string, string> Messages { get; set; } = new();
}
