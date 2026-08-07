namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// Template test result
/// </summary>
public record WebhookTemplateTestResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string?> MappedFields { get; init; } = new();
}
