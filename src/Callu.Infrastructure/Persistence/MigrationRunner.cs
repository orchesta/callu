using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence;

/// <summary>Idempotent EF Core migrate-on-startup wrapped in a Postgres advisory lock so both hosts can
/// safely call <see cref="RunAsync"/> at boot.</summary>
public static class MigrationRunner
{
    private const long AdvisoryLockKey = 0x43616C6C754D6967;

    /// <summary>
    /// Command timeout applied to the migration path only, then restored.
    /// </summary>
    internal const int MigrationCommandTimeoutSeconds = 600;

    /// <summary>Migrates under the advisory lock; underLock work (seeding) runs after it on the same
    /// context scope, so it shares the locked session.</summary>
    public static async Task RunAsync<TDbContext>(
        TDbContext db,
        ILogger logger,
        Func<Task>? underLock = null,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var connection = db.Database.GetDbConnection();
        var callerCommandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(MigrationCommandTimeoutSeconds);

        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using (var acquire = connection.CreateCommand())
                {
                    acquire.CommandText = "SELECT pg_advisory_lock(@key)";
                    acquire.CommandTimeout = MigrationCommandTimeoutSeconds;
                    var p = acquire.CreateParameter();
                    p.ParameterName = "@key";
                    p.Value = AdvisoryLockKey;
                    acquire.Parameters.Add(p);
                    logger.LogInformation("Acquiring migration advisory lock {Key}…", AdvisoryLockKey);
                    await acquire.ExecuteNonQueryAsync(cancellationToken);
                }

                try
                {
                    logger.LogInformation("Running EF Core migrations…");
                    await db.Database.MigrateAsync(cancellationToken);
                    logger.LogInformation("Migrations up to date.");

                    if (underLock is not null)
                        await underLock();
                }
                finally
                {
                    await ReleaseAdvisoryLockAsync(connection, logger, cancellationToken);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            db.Database.SetCommandTimeout(callerCommandTimeout);
        }
    }

    /// <summary>
    /// Releases the migration advisory lock; never throws.
    /// </summary>
    internal static async Task ReleaseAdvisoryLockAsync(
        DbConnection connection,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = release.CreateParameter();
            p.ParameterName = "@key";
            p.Value = AdvisoryLockKey;
            release.Parameters.Add(p);
            await release.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not release migration advisory lock {Key}. Closing the connection releases it, "
                + "so this does not block the other host.",
                AdvisoryLockKey);
        }
    }
}
