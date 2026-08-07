namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of syncing team members with provider users
/// </summary>
public record SyncResult
{
    public bool Success { get; init; }
    public int UsersCreated { get; init; }
    public int UsersDeleted { get; init; }
    public int UsersUnchanged { get; init; }
    public List<string> Errors { get; init; } = new();
}
