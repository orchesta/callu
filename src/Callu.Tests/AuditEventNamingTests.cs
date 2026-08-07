using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;

namespace Callu.Tests;

public class AuditEventNamingTests
{
    [Theory]
    [InlineData("Incident", AuditAction.Acknowledged, "incident.acknowledge")]
    [InlineData("User", AuditAction.Login, "user.login")]
    [InlineData("AuditLog", AuditAction.Exported, "audit-log.export")]
    [InlineData("OnCallOverride", AuditAction.OverrideCreated, "on-call-override.override-create")]
    [InlineData("SipTrunk", AuditAction.SettingsChanged, "sip-trunk.settings-change")]
    [InlineData("CommunicationProvider", AuditAction.Deleted, "communication-provider.delete")]
    [InlineData("Incident", AuditAction.EscalationNobodyReached, "incident.escalation-nobody-reached")]
    public void ToEventName_JoinsSlugifiedResourceAndAction(string resourceType, AuditAction action, string expected)
    {
        Assert.Equal(expected, AuditEventNaming.ToEventName(resourceType, action));
    }

    // Encoding the outcome in the name doubles the vocabulary and turns "how often does this fail?"
    // into string matching. The outcome field already answers it.
    [Theory]
    [InlineData(AuditAction.Login, AuditAction.LoginFailed)]
    [InlineData(AuditAction.IntegrityVerified, AuditAction.IntegrityBroken)]
    public void ToEventName_GivesAnOperationOneNameWhicheverWayItWent(AuditAction succeeded, AuditAction failed)
    {
        Assert.Equal(
            AuditEventNaming.ToEventName("User", succeeded),
            AuditEventNaming.ToEventName("User", failed));
    }

    // A reason is not an outcome: an auditor filters for the times nobody was reached, and these
    // would be indistinguishable if they shared one name.
    [Fact]
    public void ToEventName_KeepsTheReasonsAPageWentNowhereApart()
    {
        var names = new[]
        {
            AuditAction.EscalationNobodyReached,
            AuditAction.EscalationTargetsUnpageable,
            AuditAction.EscalationChannelsSilent,
            AuditAction.EscalationDispatchFailed,
            AuditAction.EscalationDispatchFailing,
        }.Select(a => AuditEventNaming.ToEventName("Incident", a)).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A new action must be given a name deliberately rather than inheriting a mechanical one.</summary>
    [Fact]
    public void EveryActionHasAnExplicitName()
    {
        foreach (var action in Enum.GetValues<AuditAction>())
            Assert.NotEmpty(AuditEventNaming.ToActionSegment(action));
    }

    // A zero-width regex match inserted the literal text "$1" instead of a hyphen when the
    // replacement referenced a capture group the lookaround pattern never captures.
    [Fact]
    public void ToEventName_NeverLeaksARegexGroupPlaceholder()
    {
        var name = AuditEventNaming.ToEventName("AuditLog", AuditAction.Exported);

        Assert.DoesNotContain("$1", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AuditAction.LoginFailed, AuditOutcome.Failure)]
    [InlineData(AuditAction.EscalationDispatchPartiallyFailed, AuditOutcome.Partial)]
    [InlineData(AuditAction.Acknowledged, AuditOutcome.Success)]
    public void ToOutcome_ClassifiesTheAction(AuditAction action, AuditOutcome expected)
    {
        Assert.Equal(expected, AuditEventNaming.ToOutcome(action));
    }
}
