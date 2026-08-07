using System.Text;
using Callu.Infrastructure.Messaging.Broker;
using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Callu.Infrastructure.Messaging.Outbox;

/// <summary>Publishes staged rows to the broker, claiming each one so two hosts cannot send it twice.</summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ICalluBrokerConnection broker,
    ILogger<OutboxDispatcher> logger) : BackgroundService, IOutboxNudge
{
    internal const int BatchSize = 50;
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(5);
    internal const int MaxAttempts = 12;

    private readonly SemaphoreSlim _wakeUp = new(0, 1);

    public void Nudge()
    {
        // Never blocks and never throws: the caller has already committed an incident.
        if (_wakeUp.CurrentCount == 0) _wakeUp.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await DrainAsync(stoppingToken);
                if (sent == BatchSize) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch sweep failed; rows stay claimed until their lease expires");
            }

            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(PollInterval);
                await _wakeUp.WaitAsync(wait.Token);
            }
            catch (OperationCanceledException)
            {
                // Poll interval elapsed, or the host is stopping.
            }
        }
    }

    private async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimBatchAsync(cancellationToken);
        if (claimed.Count == 0) return 0;

        await using var channel = await broker.CreatePublishChannelAsync(cancellationToken);
        await channel.ExchangeDeclareAsync(
            CalluTopology.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        var sent = 0;
        foreach (var row in claimed)
        {
            if (await PublishAsync(channel, row, cancellationToken)) sent++;
        }

        return sent;
    }

    /// <summary>Claims in a short committed transaction; the publish itself runs outside it.</summary>
    private async Task<List<OutboxEntry>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        return await transactions.ExecuteInTransactionAsync(async () =>
        {
            var batch = await db.OutboxEntries
                .FromSqlInterpolated($"""
                    SELECT *, xmin FROM "OutboxEntries"
                    WHERE "Status" IN ('Pending', 'Claimed')
                      AND "NextAttemptAt" <= NOW()
                      AND ("ClaimedUntil" IS NULL OR "ClaimedUntil" <= NOW())
                    ORDER BY "CreatedAt", "Id"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var row in batch)
            {
                row.Status = OutboxEntryStatus.Claimed;
                row.ClaimedUntil = DateTime.UtcNow.Add(ClaimLease);
                row.AttemptCount++;
                row.UpdatedAt = DateTime.UtcNow;
            }

            return batch;
        }, cancellationToken);
    }

    private async Task<bool> PublishAsync(IChannel channel, OutboxEntry row, CancellationToken cancellationToken)
    {
        if (!CalluTopology.RoutingKeys.TryGetValue(row.MessageType, out var routingKey))
        {
            await RecordFailureAsync(row.Id, $"No routing key for message type '{row.MessageType}'.", terminal: true, cancellationToken);
            return false;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = row.Id.ToString(),
            Type = row.MessageType,
            ContentType = "application/json",
        };

        if (row.TraceParent is not null)
        {
            properties.Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceparent"] = row.TraceParent,
            };
        }

        try
        {
            // mandatory: a routing key with no bound queue comes back as an exception instead of vanishing.
            await channel.BasicPublishAsync(
                exchange: CalluTopology.Exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(row.Payload),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publishing outbox row {RowId} ({MessageType}) failed", row.Id, row.MessageType);
            await RecordFailureAsync(row.Id, ex.Message, terminal: row.AttemptCount >= MaxAttempts, cancellationToken);
            return false;
        }

        await MarkSentAsync(row.Id, cancellationToken);
        return true;
    }

    private async Task MarkSentAsync(Guid rowId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.OutboxEntries
            .Where(e => e.Id == rowId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.Status, OutboxEntryStatus.Sent)
                .SetProperty(e => e.SentAt, DateTime.UtcNow)
                .SetProperty(e => e.ClaimedUntil, (DateTime?)null)
                .SetProperty(e => e.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    private async Task RecordFailureAsync(Guid rowId, string error, bool terminal, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.OutboxEntries.FirstOrDefaultAsync(e => e.Id == rowId, cancellationToken);
        if (row is null) return;

        row.LastError = error;
        row.ClaimedUntil = null;
        row.UpdatedAt = DateTime.UtcNow;

        if (terminal)
        {
            row.Status = OutboxEntryStatus.Failed;
            logger.LogError(
                "Outbox row {RowId} ({MessageType}) is given up on after {Attempts} attempts: {Error}",
                row.Id, row.MessageType, row.AttemptCount, error);
        }
        else
        {
            row.Status = OutboxEntryStatus.Pending;
            row.NextAttemptAt = DateTime.UtcNow.Add(RetryBaseDelay * Math.Pow(2, Math.Min(row.AttemptCount, 6)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _wakeUp.Dispose();
        base.Dispose();
    }
}
