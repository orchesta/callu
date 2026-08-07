using Callu.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Callu.Tests;

/// <summary>
/// The advisory-lock release runs in a finally, so anything it throws would replace the migration
/// failure the operator has to read.
/// </summary>
public class MigrationRunnerUnlockTests
{
    [Fact]
    public async Task ReleasingTheLockOnADeadConnection_DoesNotThrow()
    {
        await using var connection = new NpgsqlConnection(
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope");

        // Never opened: ExecuteNonQueryAsync on it fails without ever reaching a server, which is
        // the shape of the real case (the migration killed the session, then the unlock ran).
        await MigrationRunner.ReleaseAdvisoryLockAsync(
            connection, NullLogger.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task TheDeadConnectionPremise_HoldsForARawCommand()
    {
        await using var connection = new NpgsqlConnection(
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope");

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteNonQueryAsync());
    }
}
