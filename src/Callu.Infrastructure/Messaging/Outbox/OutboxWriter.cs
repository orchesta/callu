using System.Diagnostics;
using System.Text.Json;
using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Messaging.Outbox;

/// <summary>Stages a message for publication inside the caller's transaction.</summary>
public interface IOutboxWriter
{
    /// <summary>Adds the row to the caller's transaction without flushing; the caller's commit is what persists it.</summary>
    Guid Stage<TMessage>(string wireName, TMessage message);
}

/// <summary>Wakes the dispatcher after a commit so publication does not wait for the next poll.</summary>
public interface IOutboxNudge
{
    void Nudge();
}

public sealed class NoOpOutboxNudge : IOutboxNudge
{
    public void Nudge() { }
}

public sealed class OutboxWriter(ApplicationDbContext context) : IOutboxWriter
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public Guid Stage<TMessage>(string wireName, TMessage message)
    {
        if (!CalluTopology.RoutingKeys.ContainsKey(wireName))
            throw new ArgumentException($"'{wireName}' has no routing key in {nameof(CalluTopology)}.", nameof(wireName));

        // Without an open transaction the row could commit apart from the domain write, which is the one
        // guarantee this whole path exists for.
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{nameof(Stage)} must be called inside an open transaction so the message commits with the domain write.");

        var entry = new OutboxEntry
        {
            MessageType = wireName,
            Payload = JsonSerializer.Serialize(message, PayloadOptions),
            Status = OutboxEntryStatus.Pending,
            NextAttemptAt = DateTime.UtcNow,
            TraceParent = Activity.Current?.Id,
        };

        context.OutboxEntries.Add(entry);
        return entry.Id;
    }
}
