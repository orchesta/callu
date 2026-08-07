namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of user synchronization between CalluApp and Voximplant
/// </summary>
public record VoxUserSyncResult
{
    public int Created { get; init; }
    public int Deleted { get; init; }
    public int Unchanged { get; init; }
    public List<string> Errors { get; init; } = [];
}
