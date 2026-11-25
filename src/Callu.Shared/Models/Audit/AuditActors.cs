namespace Callu.Shared.Models.Audit;

/// <summary>The service actors callu records when no person is behind an audited act.</summary>
public static class AuditActors
{
    /// <summary>Every row an inbound alert webhook writes, whether it opens or resolves an incident.</summary>
    public const string Webhook = "service:webhook";
}
