namespace Callu.Shared.Models.Communication;

/// <summary>What a VoxEngine status callback achieved: whether escalation was asked for, and whether it paged anybody.</summary>
public readonly record struct VoxCallbackResult(bool EscalationRequested, bool EscalationPagedSomeone)
{
    /// <summary>A callback that asked for no escalation — every status other than "escalated".</summary>
    public static readonly VoxCallbackResult None = new(false, false);

    /// <summary>The responder asked to hand the incident on, and nobody was paged.</summary>
    public bool EscalationPagedNobody => EscalationRequested && !EscalationPagedSomeone;
}
