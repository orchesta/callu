namespace Callu.Domain.Enums;

/// <summary>What happens after the last escalation step if the incident is still unacknowledged.</summary>
public enum EscalationExhaustionBehavior
{
    Stop = 0,
    Repeat = 1
}
