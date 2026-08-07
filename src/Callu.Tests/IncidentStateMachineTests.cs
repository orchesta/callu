using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>Locks in the incident status transitions and their side effects so a refactor cannot widen them.</summary>
public class IncidentStateMachineTests
{
    private static Incident InStatus(IncidentStatus status) =>
        new() { Status = status, IsEscalationActive = true };

    [Fact]
    public void Acknowledge_FromOpen_SetsActorAndStopsEscalation()
    {
        var i = InStatus(IncidentStatus.Open);
        i.Acknowledge("u1");
        Assert.Equal(IncidentStatus.Acknowledged, i.Status);
        Assert.Equal("u1", i.AcknowledgedBy);
        Assert.NotNull(i.AcknowledgedAt);
        Assert.False(i.IsEscalationActive);
    }

    [Theory]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Acknowledge_FromNonOpen_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Acknowledge("u1"));

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Mitigated)]
    public void Resolve_FromNonTerminal_Resolves(IncidentStatus status)
    {
        var i = InStatus(status);
        i.Resolve("u1");
        Assert.Equal(IncidentStatus.Resolved, i.Status);
        Assert.Equal("u1", i.ResolvedBy);
        Assert.NotNull(i.ResolvedAt);
        Assert.False(i.IsEscalationActive);
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Resolve_FromTerminal_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Resolve("u1"));

    [Fact]
    public void StartInvestigation_FromOpen_AlsoAcknowledges()
    {
        var i = InStatus(IncidentStatus.Open);
        i.StartInvestigation("u1");
        Assert.Equal(IncidentStatus.Investigating, i.Status);
        Assert.Equal("u1", i.AcknowledgedBy);
        Assert.False(i.IsEscalationActive);
    }

    [Fact]
    public void StartInvestigation_FromAcknowledged_KeepsOriginalAck()
    {
        var i = InStatus(IncidentStatus.Acknowledged);
        i.AcknowledgedBy = "original";
        i.StartInvestigation("u2");
        Assert.Equal(IncidentStatus.Investigating, i.Status);
        Assert.Equal("original", i.AcknowledgedBy);
    }

    [Fact]
    public void StartInvestigation_FromMitigated_Regresses()
    {
        var i = InStatus(IncidentStatus.Mitigated);
        i.StartInvestigation("u1");
        Assert.Equal(IncidentStatus.Investigating, i.Status);
        // Only the Open path resets escalation; the Mitigated regression leaves it untouched.
        Assert.True(i.IsEscalationActive);
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void StartInvestigation_FromInvalid_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).StartInvestigation("u1"));

    [Theory]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    public void Mitigate_FromAckOrInvestigating_Works(IncidentStatus status)
    {
        var i = InStatus(status);
        i.Mitigate("u1");
        Assert.Equal(IncidentStatus.Mitigated, i.Status);
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Mitigate_FromInvalid_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Mitigate("u1"));

    [Fact]
    public void Close_FromResolved_Closes()
    {
        var i = InStatus(IncidentStatus.Resolved);
        i.Close("u1");
        Assert.Equal(IncidentStatus.Closed, i.Status);
        Assert.False(i.IsEscalationActive);
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Closed)]
    public void Close_FromNonResolved_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Close("u1"));

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Reopen_ClearsResolutionAndEscalationPointers(IncidentStatus status)
    {
        var i = InStatus(status);
        i.ResolvedBy = "x";
        i.AcknowledgedBy = "y";
        i.EscalationPolicyId = Guid.NewGuid();
        i.CurrentEscalationStepId = Guid.NewGuid();

        i.Reopen("u1");

        Assert.Equal(IncidentStatus.Open, i.Status);
        Assert.Null(i.ResolvedBy);
        Assert.Null(i.ResolvedAt);
        Assert.Null(i.AcknowledgedBy);
        Assert.Null(i.AcknowledgedAt);
        Assert.Null(i.EscalationPolicyId);
        Assert.Null(i.CurrentEscalationStepId);
        Assert.False(i.IsEscalationActive);
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Reopen_ClearsTheClosedEpisodesSuppressionFlags(IncidentStatus status)
    {
        var i = InStatus(status);
        i.IsPagingSuppressed = true;
        i.IsNotificationSuppressed = true;

        i.Reopen("u1");

        Assert.False(i.IsPagingSuppressed);
        Assert.False(i.IsNotificationSuppressed);
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    public void Reopen_FromNonTerminal_Throws(IncidentStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Reopen("u1"));

    [Fact]
    public void ChangeStatus_SameStatus_IsNoOp()
    {
        var i = InStatus(IncidentStatus.Open);
        i.ChangeStatus(IncidentStatus.Open, "u1");
        Assert.Equal(IncidentStatus.Open, i.Status);
        Assert.True(i.IsEscalationActive);
    }

    [Fact]
    public void ChangeStatus_RoutesThroughGuardedTransition()
    {
        var ok = InStatus(IncidentStatus.Open);
        ok.ChangeStatus(IncidentStatus.Acknowledged, "u1");
        Assert.Equal(IncidentStatus.Acknowledged, ok.Status);

        Assert.Throws<InvalidOperationException>(
            () => InStatus(IncidentStatus.Closed).ChangeStatus(IncidentStatus.Acknowledged, "u1"));
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved, true)]
    [InlineData(IncidentStatus.Closed, true)]
    [InlineData(IncidentStatus.Open, false)]
    [InlineData(IncidentStatus.Acknowledged, false)]
    [InlineData(IncidentStatus.Investigating, false)]
    [InlineData(IncidentStatus.Mitigated, false)]
    public void IsTerminal_FollowsLifecycle(IncidentStatus status, bool terminal)
    {
        Assert.Equal(terminal, status.IsTerminal());
        Assert.Equal(!terminal, status.IsActive());
    }

    [Fact]
    public void NumericOrder_DivergesFromLifecycle_WhyHelpersExist()
    {
        Assert.True((int)IncidentStatus.Investigating > (int)IncidentStatus.Resolved);
        Assert.False(IncidentStatus.Investigating.IsTerminal());
        Assert.True(IncidentStatus.Resolved.IsTerminal());
    }

    [Theory]
    [InlineData(IncidentStatus.Open, true)]
    [InlineData(IncidentStatus.Acknowledged, true)]
    [InlineData(IncidentStatus.Investigating, false)]
    [InlineData(IncidentStatus.Resolved, false)]
    public void IsOpenOrAcknowledged_OnlyForThoseTwo(IncidentStatus status, bool expected) =>
        Assert.Equal(expected, status.IsOpenOrAcknowledged());
}
