namespace Callu.Shared.Models.Settings;

public record FirebaseSettingsDto
{
    public Guid Id { get; init; }
    public string? ProjectId { get; init; }
    public bool HasCredential { get; init; }
    public bool IsConfigured { get; init; }
    public DateTime? LastTestedAt { get; init; }
    public string? LastTestResult { get; init; }
}

public record UpdateFirebaseSettingsRequest
{
    public string? ProjectId { get; init; }
    public string? ServiceAccountJson { get; init; }
    public bool ClearCredential { get; init; }
}

public record FirebaseTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
