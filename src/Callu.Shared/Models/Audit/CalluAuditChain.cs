namespace Callu.Shared.Models.Audit;

/// <summary>Identifiers describing Callu's internal audit hash chain.</summary>
public static class CalluAuditChain
{
    /// <summary>Written into the canonical form itself, so changing the value invalidates every stored hash.</summary>
    public const string CanonicalizationId = DetailsCanonicalizationId;

    /// <summary>Adds the writer's OpenAuditModel details, the parent span and the request method.</summary>
    public const string DetailsCanonicalizationId = "v4-details";

    /// <summary>Added the request, correlation, trace and span identifiers.</summary>
    public const string CorrelatedCanonicalizationId = "v3-correlated";

    /// <summary>What a row sealed before the version was recorded per row was hashed under.</summary>
    public const string LegacyCanonicalizationId = "v2-openauditmodel";

    public const string HashAlgorithm = "HMAC-SHA256";

    /// <summary>The canonical form a stored row was sealed under.</summary>
    public static string VersionOf(string? recorded) =>
        string.IsNullOrEmpty(recorded) ? LegacyCanonicalizationId : recorded;
}
