namespace Callu.Shared.Models.Audit;

/// <summary>Signs the canonical bytes of an exported event.</summary>
public interface IOpenAuditSigner
{
    string Algorithm { get; }
    string? KeyId { get; }
    string Sign(byte[] canonicalBytes);
}
