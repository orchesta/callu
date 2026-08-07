namespace Callu.Shared.Models.Audit;

/// <summary>An audit row shaped as a spec-conformant OpenAuditModel v0.1 event.</summary>
public sealed class OpenAuditEventEnvelope
{
    public string SpecVersion { get; init; } = "0.1";
    public Guid Id { get; init; }
    public DateTime Time { get; init; }
    public OpenAuditEventDescriptor Event { get; init; } = new();
    public OpenAuditActor Actor { get; init; } = new();
    public OpenAuditResource Resource { get; init; } = new();
    public OpenAuditApplication Application { get; init; } = new();
    public OpenAuditRequestContext? Request { get; init; }
    public OpenAuditChange? Change { get; init; }
    public long? Sequence { get; init; }
    public IReadOnlyDictionary<string, string>? Extensions { get; init; }
    public OpenAuditIntegrity? Integrity { get; init; }
}

public sealed class OpenAuditEventDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public OpenAuditErrorDescriptor? Error { get; init; }
}

/// <summary>Required when `event.outcome` is failure; permitted, but not demanded, on partial.</summary>
public sealed class OpenAuditErrorDescriptor
{
    public string Code { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public sealed class OpenAuditActor
{
    public string Type { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
}

public sealed class OpenAuditResource
{
    public string Type { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
}

public sealed class OpenAuditApplication
{
    public string Name { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}

public sealed class OpenAuditRequestContext
{
    /// <summary>The inbound request being served; never propagated into an asynchronous message.</summary>
    public string? RequestId { get; init; }

    /// <summary>The logical operation this event belongs to, which outlives any one trace.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>W3C trace id: 32 lower-case hex characters, taken from the active trace context.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C span id: 16 lower-case hex characters. Never recorded without its trace.</summary>
    public string? SpanId { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? Route { get; init; }
}

public sealed class OpenAuditChange
{
    public string? Before { get; init; }
    public string? After { get; init; }
}

/// <summary>Tamper-evidence material for the exported event.</summary>
// `Hash` is left null by the mapper and filled by the serializer, which is the only place that can
// see the finished document the digest is taken over.
public sealed class OpenAuditIntegrity
{
    public string Canonicalization { get; init; } = OpenAuditEventSerializer.Canonicalization;
    public string HashAlgorithm { get; init; } = OpenAuditEventSerializer.HashAlgorithm;
    public string? Hash { get; init; }
    public OpenAuditSignature? Signature { get; init; }
}

/// <summary>A signature over the same bytes the digest is taken over.</summary>
public sealed class OpenAuditSignature
{
    public string Algorithm { get; init; } = string.Empty;
    /// <summary>Base64, which is the only encoding a v0.1 verifier checks.</summary>
    public string Value { get; init; } = string.Empty;
    /// <summary>Names the key, and never carries key material.</summary>
    public string? KeyId { get; init; }
}

/// <summary>Signs the canonical bytes of an exported event.</summary>
public interface IOpenAuditSigner
{
    string Algorithm { get; }
    string? KeyId { get; }
    string Sign(byte[] canonicalBytes);
}
