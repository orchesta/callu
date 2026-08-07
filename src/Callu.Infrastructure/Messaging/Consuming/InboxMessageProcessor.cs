using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Callu.Infrastructure.Messaging.Consuming;

/// <summary>What the transport should do with a delivery once the processor is done with it.</summary>
public enum DeliveryOutcome
{
    /// <summary>Handled, or a duplicate of something already handled.</summary>
    Ack,

    /// <summary>Not handled; the broker should hand it back.</summary>
    Requeue,

    /// <summary>Uninterpretable; another delivery cannot help.</summary>
    DeadLetter,
}

/// <summary>Runs one delivery: dedupe row, handler, flush, commit — the order a page depends on.</summary>
public interface IInboxMessageProcessor
{
    Task<DeliveryOutcome> ProcessAsync(
        Guid messageId,
        string messageType,
        string payload,
        string? traceParent,
        CancellationToken cancellationToken);
}

public sealed class InboxMessageProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<InboxMessageProcessor> logger) : IInboxMessageProcessor
{
    private const string UniqueViolation = "23505";

    public async Task<DeliveryOutcome> ProcessAsync(
        Guid messageId,
        string messageType,
        string payload,
        string? traceParent,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var handler = ResolveHandler(scope.ServiceProvider, messageType);

        if (handler is null)
        {
            logger.LogError("No handler is registered for message type {MessageType}; dead-lettering it", messageType);
            return DeliveryOutcome.DeadLetter;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            try
            {
                db.InboxEntries.Add(new InboxEntry
                {
                    Id = messageId,
                    MessageType = messageType,
                    Payload = payload,
                    Status = InboxEntryStatus.Consumed,
                    ConsumedAt = DateTime.UtcNow,
                    NextAttemptAt = DateTime.UtcNow,
                    TraceParent = traceParent,
                });

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);

                // The row is the proof that an earlier delivery handled it; its state does not matter here.
                logger.LogDebug("Duplicate delivery {MessageId} ({MessageType}) ignored", messageId, messageType);
                return DeliveryOutcome.Ack;
            }

            await handler.HandleAsync(payload, cancellationToken);

            // This processor opened the transaction, so the flush is its own. Without it the inbox row commits
            // and the handler's writes leave with the change tracker — the message acked, never redelivered.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return DeliveryOutcome.Ack;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(transaction, messageId);

            // The retry lives in the row, not in the broker: an immediate requeue would hot-loop on a
            // persistent failure and, at prefetch 1, hold every other incident behind this one.
            var persisted = await TryPersistRetryAsync(messageId, messageType, payload, traceParent, ex, cancellationToken);

            if (persisted)
            {
                logger.LogError(ex,
                    "Handling {MessageType} ({MessageId}) failed; queued for retry from the inbox row",
                    messageType, messageId);
                return DeliveryOutcome.Ack;
            }

            // The row could not be written either, so the database is the likely cause: let the broker keep it.
            logger.LogError(ex,
                "Handling {MessageType} ({MessageId}) failed and its retry row could not be written; "
                + "the broker will hand it back", messageType, messageId);
            return DeliveryOutcome.Requeue;
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    /// <summary>Writes the failed delivery as a retryable row in its own short transaction.</summary>
    private async Task<bool> TryPersistRetryAsync(
        Guid messageId,
        string messageType,
        string payload,
        string? traceParent,
        Exception failure,
        CancellationToken cancellationToken)
    {
        var terminal = false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existing = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == messageId, cancellationToken);

            if (existing is null)
            {
                db.InboxEntries.Add(new InboxEntry
                {
                    Id = messageId,
                    MessageType = messageType,
                    Payload = payload,
                    Status = InboxEntryStatus.Retrying,
                    AttemptCount = 1,
                    NextAttemptAt = DateTime.UtcNow.Add(InboxEntry.BackoffFor(1)),
                    LastError = failure.Message,
                    TraceParent = traceParent,
                });
            }
            else
            {
                existing.AttemptCount++;
                existing.LastError = failure.Message;
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.AttemptCount >= InboxEntry.MaxAttempts)
                {
                    existing.Status = InboxEntryStatus.Failed;
                    logger.LogError(
                        "Message {MessageId} ({MessageType}) is given up on after {Attempts} attempts",
                        messageId, messageType, existing.AttemptCount);
                    terminal = true;
                }
                else
                {
                    existing.Status = InboxEntryStatus.Retrying;
                    existing.NextAttemptAt = DateTime.UtcNow.Add(InboxEntry.BackoffFor(existing.AttemptCount));
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            if (terminal)
            {
                var handler = ResolveHandler(scope.ServiceProvider, messageType);

                if (handler is not null)
                {
                    try
                    {
                        await handler.ReportPermanentFailureAsync(payload, failure.Message, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Reporting the terminal failure of {MessageType} threw", messageType);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist the retry row for {MessageId}", messageId);
            return false;
        }
    }

    private async Task SafeRollbackAsync(IDisposable transaction, Guid messageId)
    {
        try
        {
            if (transaction is Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
                await tx.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The database is the likely cause of the original failure; a failed rollback must not mask it.
            logger.LogWarning(ex, "Rolling back after a failed delivery {MessageId} also failed", messageId);
        }
    }

    /// <summary>The allow-list lookup, in one place so the guard has one statement to account for.</summary>
    private static ICalluMessageHandler? ResolveHandler(IServiceProvider services, string messageType) =>
        services.GetServices<ICalluMessageHandler>()
            .FirstOrDefault(h => string.Equals(h.WireName, messageType, StringComparison.Ordinal));

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
