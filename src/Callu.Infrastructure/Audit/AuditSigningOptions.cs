namespace Callu.Infrastructure.Audit;

/// <summary>Binds to the <c>Callu:AuditSigning</c> section.</summary>
// Off by default: a signature is only worth adding when somebody is going to check it, and turning
// it on means taking on key custody.
public class AuditSigningOptions
{
    public const string SectionName = "Callu:AuditSigning";

    public bool Enabled { get; set; }
}
