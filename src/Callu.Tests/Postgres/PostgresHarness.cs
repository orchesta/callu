using System.Runtime.InteropServices;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Callu.Tests;

/// <summary>Discovery-time probe for a reachable Docker daemon.</summary>
// It checks only that the endpoint exists; a wedged daemon surfaces as a container-start failure instead.
internal static class DockerAvailability
{
    public const string SkipReason =
        "Docker is not reachable — skipping the PostgreSQL-backed integration tests. "
        + "Set CALLU_REQUIRE_DOCKER=1 (CI does) to turn this skip into a failure.";

    /// <summary>Set in CI, where a missing Docker endpoint must fail rather than skip.</summary>
    public const string RequireDockerVariable = "CALLU_REQUIRE_DOCKER";

    public static bool Required { get; } =
        Environment.GetEnvironmentVariable(RequireDockerVariable) is "1" or "true" or "TRUE";

    public static bool IsAvailable { get; } = Probe();

    /// <summary>True when the Postgres-backed tests are being skipped, so callers can say so out loud.</summary>
    public static bool IsSkipping => !IsAvailable && !Required;

    private static bool Probe()
    {
        var available = ProbeCore();

        if (!available && !Required)
            Console.Error.WriteLine(
                $"[callu-tests] WARNING: {SkipReason} "
                + "The PostgreSQL-backed tests (migrations, notification claim race, transaction "
                + "outbox, refresh-token rotation, first-admin setup) did NOT run.");

        return available;
    }

    private static bool ProbeCore()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return File.Exists("/var/run/docker.sock");

        try
        {
            return Directory
                .GetFiles(@"\\.\pipe\")
                .Any(pipe => pipe.Contains("docker_engine", StringComparison.OrdinalIgnoreCase)
                          || pipe.Contains("dockerDesktopLinuxEngine", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips itself when Docker is unavailable and not required.</summary>
// xUnit v2 has no runtime skip, so the decision has to be made at discovery.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (DockerAvailability.IsSkipping)
            Skip = DockerAvailability.SkipReason;
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips itself when Docker is unavailable and not required.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (DockerAvailability.IsSkipping)
            Skip = DockerAvailability.SkipReason;
    }
}

/// <summary>Makes the Docker skip a local-developer convenience rather than a CI mode.</summary>
public class DockerAvailabilityTests
{
    [Fact]
    public void WhenDockerIsRequired_ItIsActuallyThere()
    {
        if (!DockerAvailability.Required)
            return;

        Assert.True(
            DockerAvailability.IsAvailable,
            $"{DockerAvailability.RequireDockerVariable} is set but no Docker endpoint was found. "
            + "The PostgreSQL-backed tests would have been skipped, leaving the migration DDL, the "
            + "notification claim race and the transaction outbox unexercised.");
    }
}

/// <summary>
/// One PostgreSQL container shared by the collection; each test gets a freshly created, empty
/// database inside it, so migration runs and concurrency races never see each other's state.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// Pinned to the server version <c>docker-compose.yml</c> ships. Migrations must be exercised
    /// against the engine self-hosters actually run, not a newer one that is more forgiving.
    /// </summary>
    private const string Image = "postgres:16.14-alpine";

    private readonly PostgreSqlContainer? _container;

    public PostgresFixture()
    {
        if (!DockerAvailability.IsAvailable)
            return;

        _container = new PostgreSqlBuilder(Image)
            .WithDatabase("callu")
            .WithUsername("callu")
            .WithPassword("callu")
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_container is not null)
            await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Creates an empty database and returns a connection string pointing at it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        if (_container is null)
            throw new InvalidOperationException(
                $"{DockerAvailability.RequireDockerVariable} is set but Docker is not reachable, so no "
                + "PostgreSQL container was started. These tests are the only coverage the migration "
                + "DDL, the notification claim race and the transaction outbox have.");

        var name = $"t{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{name}" """, admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,

            // A database per test means a pool per test, and each pool holds idle connections for five
            // minutes — enough of them to exhaust the server's ceiling mid-run. These tests run serially.
            Pooling = false
        }.ConnectionString;
    }

    /// <summary>
    /// A context wired exactly like production persistence (Npgsql + NodaTime + the migrations
    /// assembly), so what the test migrates is what a self-hosted instance migrates.
    /// </summary>
    public static ApplicationDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.UseNodaTime();
            })
            .Options);

    /// <summary>Brings a database up to the current head migration.</summary>
    public static async Task MigrateToHeadAsync(string connectionString)
    {
        await using var db = Context(connectionString);
        await db.Database.MigrateAsync();
    }
}

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
