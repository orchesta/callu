namespace Callu.Domain.Enums;

/// <summary>Whether a checkpoint's anchor has been made; only issued ones are shown or proven against.</summary>
public enum AuditCheckpointStatus
{
    Pending = 1,
    Issued = 2,
}
