using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>
/// A mitigation that regresses goes Mitigated → Investigating directly, without re-activating
/// escalation and re-paging people who already responded.
/// </summary>
public class IncidentMitigatedRegressionTests
{
    private static Incident Mitigated()
    {
        var incident = new Incident { Title = "db latency spike" };
        incident.StartInvestigation("responder");  // Open → Investigating
        incident.Mitigate("responder");            // Investigating → Mitigated
        return incident;
    }

    [Fact]
    public void StartInvestigation_is_allowed_from_Mitigated_without_repaging()
    {
        var incident = Mitigated();
        var policyId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        incident.EscalationPolicyId = policyId;
        incident.CurrentEscalationStepId = stepId;
        incident.IsEscalationActive = false;

        incident.StartInvestigation("responder");

        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        // No fresh paging wave: escalation is neither re-activated nor re-staged.
        Assert.False(incident.IsEscalationActive);
        Assert.Equal(policyId, incident.EscalationPolicyId);
        Assert.Equal(stepId, incident.CurrentEscalationStepId);
    }

    [Fact]
    public void ChangeStatus_routes_Mitigated_to_Investigating()
    {
        var incident = Mitigated();

        incident.ChangeStatus(IncidentStatus.Investigating, "responder");

        Assert.Equal(IncidentStatus.Investigating, incident.Status);
    }

    [Fact]
    public void StartInvestigation_still_rejected_from_Resolved()
    {
        var incident = new Incident { Title = "x" };
        incident.Resolve("responder");

        Assert.Throws<InvalidOperationException>(() => incident.StartInvestigation("responder"));
    }
}
