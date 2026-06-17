using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence;

/// <summary>
/// Idempotent EF Core migrate-on-startup wrapped in a Postgres advisory lock so the
/// API and Worker hosts can both safely call <see cref="RunAsync"/> at boot. Without
/// the lock, the first concurrent startup races on <c>__EFMigrationsHistory</c> and
/// one of the hosts crash-loops on a duplicate-key violation.
///
/// Lock key (constant 64-bit int) is arbitrary but must match across hosts. Chosen
/// to be globally unique within the application's advisory-lock namespace — collisions
/// with Postgres internals or other Callu lock-using code are unlikely.
/// </summary>
public static class MigrationRunner
{
    private const long AdvisoryLockKey = 0x43616C6C754D6967;

    public static async Task RunAsync<TDbContext>(TDbContext db, ILogger logger, CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using (var acquire = connection.CreateCommand())
            {
                acquire.CommandText = "SELECT pg_advisory_lock(@key)";
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
            }
            finally
            {
                await using var release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(@key)";
                var p = release.CreateParameter();
                p.ParameterName = "@key";
                p.Value = AdvisoryLockKey;
                release.Parameters.Add(p);
                await release.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
