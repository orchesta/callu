namespace Callu.Infrastructure.Services.Models;

/// <summary>
/// Result of template validation
/// </summary>
public class TemplateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, string?> ExtractedFields { get; set; } = new();
}
