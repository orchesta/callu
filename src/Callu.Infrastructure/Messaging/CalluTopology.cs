namespace Callu.Infrastructure.Messaging;

/// <summary>The broker names and the wire contract, in one place because two hosts declare against them.</summary>
public static class CalluTopology
{
    public const string Exchange = "callu";
    public const string DeadLetterExchange = "callu.dlx";

    public const string EscalationRoutingKey = "incident.escalation.trigger";
    public const string StatusPageNotifyRoutingKey = "statuspage.subscribers.notify";

    public const string EscalationQueue = "callu.incident.escalation";
    public const string StatusPageNotifyQueue = "callu.statuspage.notify";

    public const string EscalationDeadLetterQueue = "callu.incident.escalation.dlq";
    public const string StatusPageNotifyDeadLetterQueue = "callu.statuspage.notify.dlq";

    /// <summary>Stable wire names. Never a CLR or assembly-qualified type name: renaming a class would strand messages.</summary>
    public const string TriggerIncidentEscalationMessage = "incident.escalation.trigger.v1";
    public const string NotifyStatusPageSubscribersMessage = "statuspage.subscribers.notify.v1";

    /// <summary>Wire name to routing key. A message type absent here cannot be published.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoutingKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TriggerIncidentEscalationMessage] = EscalationRoutingKey,
        [NotifyStatusPageSubscribersMessage] = StatusPageNotifyRoutingKey,
    };

    /// <summary>Queue to the wire names it carries, so the consumer host binds and dispatches from one source.</summary>
    public static readonly IReadOnlyDictionary<string, string> QueueRoutingKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EscalationQueue] = EscalationRoutingKey,
        [StatusPageNotifyQueue] = StatusPageNotifyRoutingKey,
    };

    public static readonly IReadOnlyDictionary<string, string> DeadLetterQueues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EscalationQueue] = EscalationDeadLetterQueue,
        [StatusPageNotifyQueue] = StatusPageNotifyDeadLetterQueue,
    };

    /// <summary>Arguments every main queue declares. Deliberately no x-message-ttl and no x-max-length:
    /// either one drops a page silently.</summary>
    public static Dictionary<string, object?> MainQueueArguments() => new(StringComparer.Ordinal)
    {
        ["x-dead-letter-exchange"] = DeadLetterExchange,
    };
}
