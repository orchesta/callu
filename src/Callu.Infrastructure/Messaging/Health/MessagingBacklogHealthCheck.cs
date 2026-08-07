using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Callu.Infrastructure.Messaging.Health;

/// <summary>Counts the messages that have not been delivered yet, or have been given up on.</summary>
public sealed class MessagingBacklogHealthCheck(IDbContextFactory<ApplicationDbContext> contextFactory) : IHealthCheck
{
    public const string Name = "messaging-backlog";

    internal const int QueryTimeoutSeconds = 5;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            db.Database.SetCommandTimeout(QueryTimeoutSeconds);

            var waiting = await db.OutboxEntries.CountAsync(
                e => e.Status == OutboxEntryStatus.Pending || e.Status == OutboxEntryStatus.Claimed,
                cancellationToken);
            var undelivered = await db.OutboxEntries.CountAsync(
                e => e.Status == OutboxEntryStatus.Failed, cancellationToken);
            var retrying = await db.InboxEntries.CountAsync(
                e => e.Status == InboxEntryStatus.Retrying, cancellationToken);
            var abandoned = await db.InboxEntries.CountAsync(
                e => e.Status == InboxEntryStatus.Failed, cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["outboxWaiting"] = waiting,
                ["outboxUndelivered"] = undelivered,
                ["inboxRetrying"] = retrying,
                ["inboxAbandoned"] = abandoned,
            };

            // Reporting only: a backlog is a number an operator reads, not a reason to fail a gate.
            // Waiting rows are normal at any instant; the ones that matter are the ones still here later.
            return HealthCheckResult.Healthy(
                $"outbox waiting {waiting}, undelivered {undelivered}; "
                + $"inbox retrying {retrying}, abandoned {abandoned}",
                data);
        }
        catch (Exception ex)
        {
            // Degraded, not Unhealthy: not knowing the backlog is not the same as the messaging path
            // being broken, and this check is never on the readiness gate.
            return HealthCheckResult.Degraded("Messaging backlog could not be read", ex);
        }
    }
}
