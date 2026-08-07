using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Messaging.Consuming;

/// <summary>Re-runs deliveries whose handler failed, from the row rather than from the broker.</summary>
public interface IInboxRetrySweep
{
    Task<int> SweepAsync(CancellationToken cancellationToken);
}

public sealed class InboxRetrySweep(
    IServiceScopeFactory scopeFactory,
    ILogger<InboxRetrySweep> logger) : IInboxRetrySweep
{
    internal const int BatchSize = 50;

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var due = await ClaimDueAsync(cancellationToken);
        var handled = 0;

        foreach (var row in due)
        {
            if (await RunAsync(row, cancellationToken)) handled++;
        }

        return handled;
    }

    /// <summary>Claims in a short committed transaction so the handler runs outside it, like the notification sweep.</summary>
    private async Task<List<InboxEntry>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        return await transactions.ExecuteInTransactionAsync(async () =>
        {
            var batch = await db.InboxEntries
                .FromSqlInterpolated($"""
                    SELECT *, xmin FROM "InboxEntries"
                    WHERE "Status" = 'Retrying'
                      AND "NextAttemptAt" <= NOW()
                      AND "AttemptCount" < {InboxEntry.MaxAttempts}
                    ORDER BY "NextAttemptAt", "Id"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            // Pushed out of the way so a second sweep in the same window does not take the same row.
            foreach (var row in batch)
            {
                row.NextAttemptAt = DateTime.UtcNow.Add(InboxEntry.BackoffFor(row.AttemptCount + 1));
                row.UpdatedAt = DateTime.UtcNow;
            }

            return batch;
        }, cancellationToken);
    }

    private async Task<bool> RunAsync(InboxEntry claimed, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var handler = ResolveHandler(scope.ServiceProvider, claimed.MessageType);

        var row = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == claimed.Id, cancellationToken);
        if (row is null) return false;

        if (handler is null)
        {
            row.Status = InboxEntryStatus.Failed;
            row.LastError = $"No handler is registered for '{claimed.MessageType}'.";
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Inbox row {MessageId} has no handler for {MessageType} and is given up on", row.Id, row.MessageType);
            return false;
        }

        using var activity = MessagingActivity.StartConsume(row.MessageType, row.TraceParent);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await handler.HandleAsync(row.Payload, cancellationToken);

            row.Status = InboxEntryStatus.Consumed;
            row.ConsumedAt = DateTime.UtcNow;
            row.AttemptCount++;
            row.UpdatedAt = DateTime.UtcNow;

            // The handler's writes and the row's new state belong to one commit.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RecordAttemptFailureAsync(claimed.Id, ex, cancellationToken);
            return false;
        }
    }

    private async Task RecordAttemptFailureAsync(Guid messageId, Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == messageId, cancellationToken);
            if (row is null) return;

            row.AttemptCount++;
            row.LastError = failure.Message;
            row.UpdatedAt = DateTime.UtcNow;

            var terminal = row.AttemptCount >= InboxEntry.MaxAttempts;

            if (terminal)
            {
                row.Status = InboxEntryStatus.Failed;
                logger.LogError(failure,
                    "Message {MessageId} ({MessageType}) is given up on after {Attempts} attempts; "
                    + "whatever it was going to do has not happened",
                    row.Id, row.MessageType, row.AttemptCount);
            }
            else
            {
                row.NextAttemptAt = DateTime.UtcNow.Add(InboxEntry.BackoffFor(row.AttemptCount));
            }

            await db.SaveChangesAsync(cancellationToken);

            if (terminal)
            {
                await ReportTerminalAsync(scope.ServiceProvider, row.MessageType, row.Payload, failure.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Detach-and-move-on: one unwritable row must not lose the rest of the sweep's records.
            logger.LogWarning(ex, "Could not record the failed attempt for inbox row {MessageId}", messageId);
        }
    }

    /// <summary>Hands the terminal failure to the handler, which knows what record its message owes an operator.</summary>
    private async Task ReportTerminalAsync(
        IServiceProvider services,
        string messageType,
        string payload,
        string reason,
        CancellationToken cancellationToken)
    {
        var handler = ResolveHandler(services, messageType);

        if (handler is null) return;

        try
        {
            await handler.ReportPermanentFailureAsync(payload, reason, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reporting the terminal failure of {MessageType} threw", messageType);
        }
    }

    /// <summary>The allow-list lookup, in one place so the guard has one statement to account for.</summary>
    private static ICalluMessageHandler? ResolveHandler(IServiceProvider services, string messageType) =>
        services.GetServices<ICalluMessageHandler>()
            .FirstOrDefault(h => string.Equals(h.WireName, messageType, StringComparison.Ordinal));
}
