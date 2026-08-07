using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Callu.Tests;

/// <summary>The migrations, run as real DDL against a real PostgreSQL server through the boot path.</summary>
// Migrations auto-run on startup, so a destructive one has to fail here rather than in production.
[Collection(PostgresCollection.Name)]
public class MigrationIntegrationTests(PostgresFixture pg)
{
    /// <summary>A column as PostgreSQL itself reports it, not as EF believes it to be.</summary>
    private sealed record Column(
        string Table,
        string Name,
        bool IsNullable,
        bool HasDefault,
        string DataType,
        int? MaxLength);

    private sealed record Step(string Migration, List<Column> Before, List<Column> After);

    // v1.0.0 is a fresh baseline: a single `Initial` migration, no previous released schema to
    // upgrade FROM. The "upgrade a populated pre-baseline database" tests were removed with the old
    // migrations; the boot-path and Golden-Rules tests below still guard every FUTURE migration.

    // ---- boot path ---------------------------------------------------------

    [PostgresFact]
    public async Task FreshDatabase_MigratesCleanly_ThroughTheRealBootPath()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(cs);

        await MigrationRunner.RunAsync(db, NullLogger.Instance);

        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The migrated schema does not match the EF model.");
    }

    [PostgresFact]
    public async Task MigrateOnStartup_IsIdempotent_SoARestartIsSafe()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(cs);

        await MigrationRunner.RunAsync(db, NullLogger.Instance);
        await MigrationRunner.RunAsync(db, NullLogger.Instance);

        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }

    /// <summary>
    /// Npgsql's 30 s default command timeout kills both the DDL on a large table and the blocking
    /// advisory-lock wait behind the other host's migration.
    /// </summary>
    [PostgresFact]
    public async Task TheMigrationPath_RunsUnderItsOwnLongCommandTimeout()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(cs);

        var defaultTimeout = db.Database.GetCommandTimeout();
        int? observed = null;

        await MigrationRunner.RunAsync(db, NullLogger.Instance, () =>
        {
            observed = db.Database.GetCommandTimeout();
            return Task.CompletedTask;
        });

        Assert.Equal(MigrationRunner.MigrationCommandTimeoutSeconds, observed);
        Assert.True(observed > 30, "the whole point is that it is not Npgsql's 30 s default");
        Assert.Equal(defaultTimeout, db.Database.GetCommandTimeout());
    }

    [PostgresFact]
    public async Task EveryMigration_AppliesOnTopOfTheOneBeforeIt()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(cs);
        var migrator = db.GetService<IMigrator>();

        // One at a time rather than a single jump to head: a version-tolerant upgrade has to
        // survive every intermediate schema, and this is where a broken step surfaces.
        foreach (var migration in db.Database.GetMigrations())
            await migrator.MigrateAsync(migration);

        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }


    // ---- Golden Rules, enforced against every migration --------------------

    [PostgresFact]
    public async Task NoMigration_AddsARequiredColumnToAnExistingTable()
    {
        // A NOT NULL column with no default cannot be added to a table that already holds rows —
        // Postgres rejects the ALTER and the instance crash-loops on boot.
        var violations =
            from step in await EvolveAsync()
            let tablesThatMayHoldRows = step.Before.Select(c => c.Table).ToHashSet()
            let known = step.Before.Select(c => (c.Table, c.Name)).ToHashSet()
            from column in step.After
            where tablesThatMayHoldRows.Contains(column.Table)
               && !known.Contains((column.Table, column.Name))
               && !column.IsNullable
               && !column.HasDefault
            select $"{step.Migration}: {column.Table}.{column.Name} is NOT NULL with no default";

        AssertNoViolations(violations);
    }

    [PostgresFact]
    public async Task NoMigration_DropsOrRenamesAnExistingColumn()
    {
        // Renames look like a drop plus an add, so this catches both. Either one destroys data on
        // an instance that is mid-upgrade.
        var violations =
            from step in await EvolveAsync()
            let surviving = step.After.Select(c => (c.Table, c.Name)).ToHashSet()
            from column in step.Before
            where !surviving.Contains((column.Table, column.Name))
            select $"{step.Migration}: {column.Table}.{column.Name} was dropped";

        AssertNoViolations(violations);
    }

    [PostgresFact]
    public async Task NoMigration_ShrinksAnExistingColumn()
    {
        // Narrowing a type (varchar(1000) → varchar(100)) fails the migration outright as soon as
        // one stored row is longer than the new limit.
        var violations =
            from step in await EvolveAsync()
            from before in step.Before
            where before.MaxLength is not null
            from after in step.After
            where after.Table == before.Table
               && after.Name == before.Name
               && after.MaxLength < before.MaxLength
            select $"{step.Migration}: {before.Table}.{before.Name} shrank from "
                 + $"{before.MaxLength} to {after.MaxLength}";

        AssertNoViolations(violations);
    }

    private static void AssertNoViolations(IEnumerable<string> violations)
    {
        var found = violations.ToList();

        Assert.True(
            found.Count == 0,
            "Migration Golden Rule violation (see CONTRIBUTING.md — migrations auto-run on boot):"
            + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Applies every migration one at a time on a fresh database, capturing the real schema either
    /// side of each step.
    /// </summary>
    private async Task<List<Step>> EvolveAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(cs);
        var migrator = db.GetService<IMigrator>();

        var steps = new List<Step>();
        var before = await SnapshotAsync(cs);

        foreach (var migration in db.Database.GetMigrations())
        {
            await migrator.MigrateAsync(migration);
            var after = await SnapshotAsync(cs);
            steps.Add(new Step(migration, before, after));
            before = after;
        }

        // Without this the three Golden Rule tests would pass vacuously if the migrations
        // assembly ever failed to load.
        Assert.NotEmpty(steps);
        return steps;
    }

    private static async Task<List<Column>> SnapshotAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT table_name, column_name, is_nullable, column_default IS NOT NULL,
                   data_type, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            """,
            conn);

        var columns = new List<Column>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new Column(
                Table: reader.GetString(0),
                Name: reader.GetString(1),
                IsNullable: reader.GetString(2) == "YES",
                HasDefault: reader.GetBoolean(3),
                DataType: reader.GetString(4),
                MaxLength: reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return columns;
    }
}
