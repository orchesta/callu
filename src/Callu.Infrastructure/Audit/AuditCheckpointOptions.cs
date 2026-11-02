namespace Callu.Infrastructure.Audit;

/// <summary>Binds to <c>Callu:AuditCheckpoint</c>; no URL means no automatic checkpoints.</summary>
// A checkpoint must name where it was placed beyond callu's reach. Without somewhere to place it,
// callu issues none rather than one claiming an anchor that does not exist.
public class AuditCheckpointOptions
{
    public const string SectionName = "Callu:AuditCheckpoint";

    /// <summary>Receives a POST of {checkpoint, root}; should be under different administrative control.</summary>
    public string? PublishUrl { get; set; }

    /// <summary>Sent as a bearer token when set.</summary>
    public string? PublishToken { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(PublishUrl);
}
