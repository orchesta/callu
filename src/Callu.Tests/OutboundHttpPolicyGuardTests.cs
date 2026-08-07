using System.Text.RegularExpressions;

namespace Callu.Tests;

/// <summary>
/// Two properties of the outbound HTTP wiring that have no behavioural seam: a socket connect and a
/// DI-time resilience policy.
/// </summary>
public class OutboundHttpPolicyGuardTests
{
    private static string Module() => SourceScanner.Code(
        SourceScanner.ProductFiles(includeMigrations: false)
            .Single(f => Path.GetFileName(f) == "CommunicationModule.cs"));

    /// <summary>
    /// One dead node of a multi-homed SMS gateway must not fail a page while a healthy address for
    /// the same host sits one slot away.
    /// </summary>
    [Fact]
    public void TheIpPinnedConnect_TriesEveryAllowedAddress()
    {
        var code = Module();

        Assert.DoesNotContain("Array.Find(addresses", code, StringComparison.Ordinal);
        Assert.Contains("Array.FindAll(addresses", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"foreach\s*\(\s*var\s+ip\s+in\s+allowed\s*\)"), code);
    }

    /// <summary>The SSRF pin itself must stay per-address — iterating must not have widened it.</summary>
    [Fact]
    public void EveryCandidateAddress_IsStillCheckedAgainstTheAllowlist()
    {
        Assert.Contains("UrlSanitizer.IsAllowedTargetIp(a, allowPrivate)", Module(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Transport retry on a POST the receiver cannot dedupe multiplies one ACK by the HTTP retries
    /// and again by the durable WebhookDelivery ladder.
    /// </summary>
    [Fact]
    public void TheWebhookClient_DoesNotRetryAtTheHttpLayer()
    {
        var code = Module();

        Assert.Matches(
            new Regex(@"webhookBuilder\s*\.\s*AddStandardResilienceHandler\(\s*ConfigureNonIdempotentProvider\s*\)"),
            code);
        Assert.DoesNotContain("options.Retry.MaxRetryAttempts", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared policy is what all four money/paging clients lean on, so its retry-off switch is
    /// the single line that must never soften.
    /// </summary>
    [Fact]
    public void TheNonIdempotentPolicy_ReallyDisablesRetries()
    {
        Assert.Matches(
            new Regex(@"options\s*\.\s*Retry\s*\.\s*ShouldHandle\s*=\s*_\s*=>\s*ValueTask\.FromResult\(false\)"),
            Module());
    }

    /// <summary>
    /// A retry the receiver CAN recognise: the body is byte-identical across attempts, so without a
    /// stable key it has no way to collapse them.
    /// </summary>
    [Fact]
    public void TheAckRequest_CarriesAStableIdempotencyKey()
    {
        var dispatcher = SourceScanner.Code(
            SourceScanner.ProductFiles(includeMigrations: false)
                .Single(f => Path.GetFileName(f) == "IncidentEventDispatcher.cs"));

        Assert.Contains("DeliveryKeyHeader", dispatcher, StringComparison.Ordinal);

        // Reserved against operator-supplied headers, or an ACK could carry two different values.
        Assert.Matches(
            new Regex(@"key\.Equals\(DeliveryKeyHeader,\s*StringComparison\.OrdinalIgnoreCase\)\)\s*continue;"),
            dispatcher);
    }
}
