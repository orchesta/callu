using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Callu.Api.Controllers;
using Callu.Api.Services;
using Callu.Application.Common.Interfaces;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Callu.Shared.Models.Settings;
using Npgsql;
using NSubstitute;
using Callu.Application.Services;

namespace Callu.Tests;

/// <summary>Refresh-token rotation and first-admin setup, raced against a real PostgreSQL server.</summary>
// Both defences are row locks, advisory locks and serialization failures, none of which EF in-memory has.
[Collection(PostgresCollection.Name)]
public class PostgresConcurrencyTests(PostgresFixture pg)
{
    private const string Password = "Str0ng-Passw0rd!";
    private const int Racers = 8;

    // ---- refresh-token rotation --------------------------------------------

    [PostgresFact]
    public async Task ConcurrentRotationOfOneToken_LetsExactlyOneCallerWin()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var tokenId = await SeedRefreshTokenAsync(cs, "hash", Guid.NewGuid());

        // Every racer issues the same conditional UPDATE against the same row.
        var won = await Task.WhenAll(Enumerable.Range(0, Racers).Select(async i =>
        {
            await using var db = PostgresFixture.Context(cs);
            var repo = new RefreshTokenRepository(db, NullLogger<RefreshTokenRepository>.Instance);

            return await repo.TryRevokeForRotationAsync(tokenId, DateTime.UtcNow, $"replacement-{i}");
        }));

        Assert.Equal(1, won.Count(w => w));
    }

    [PostgresFact]
    public async Task ConcurrentRefreshOfOneToken_IssuesExactlyOneReplacement()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        const string plaintext = "the-one-refresh-token";
        var family = Guid.NewGuid();
        await SeedRefreshTokenAsync(cs, HashToken(plaintext), family);

        var responses = await Task.WhenAll(Enumerable.Range(0, Racers).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(cs);
            return await AuthService(db).RefreshTokenAsync(plaintext);
        }));

        Assert.Equal(1, responses.Count(r => r.Success));

        // The losers detect the lost rotation as token reuse and revoke the family — they must not
        // mint a token of their own. Two rows: the original and the winner's replacement.
        Assert.Equal(2L, await ScalarAsync<long>(cs, """SELECT COUNT(*) FROM "RefreshTokens" """));
    }

    // ---- first-admin setup --------------------------------------------------

    /// <summary>Two replicas racing the anonymous first-admin endpoint must produce exactly one admin.</summary>
    // The loser is asserted only as "not a success", because its status code is not yet the intended 400.
    [PostgresFact]
    public async Task ConcurrentInitialSetup_CreatesExactlyOneAdmin()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var app = BuildIdentityApp(cs);

        // Each replica asks for a different admin email, so "exactly one" cannot be an accident of
        // the unique-email index — it has to come from the lock.
        var results = await Task.WhenAll(
            InitialSetupAsync(app, "first@example.io"),
            InitialSetupAsync(app, "second@example.io"));

        Assert.Equal(1, results.Count(r => r is OkObjectResult));
        Assert.Equal(1, results.Count(r => r is not OkObjectResult));
        Assert.Equal(1L, await CountAdminsAsync(cs));

        // Whichever replica lost, it must not have left a half-created user behind.
        Assert.Equal(1L, await ScalarAsync<long>(cs, """SELECT COUNT(*) FROM "AspNetUsers" """));
    }

    [PostgresFact]
    public async Task InitialSetup_SelfDisables_OnceAnAdminExists()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var app = BuildIdentityApp(cs);

        var first = await InitialSetupAsync(app, "admin@example.io");
        var second = await InitialSetupAsync(app, "usurper@example.io");

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<BadRequestObjectResult>(second);
        Assert.Equal(1L, await CountAdminsAsync(cs));

        // The endpoint is anonymous — the only thing standing between the internet and a second
        // admin is this check, so verify the usurper was not created at all.
        Assert.Equal(0L, await ScalarAsync<long>(
            cs, """SELECT COUNT(*) FROM "AspNetUsers" WHERE "Email" = 'usurper@example.io'"""));
    }

    [PostgresFact]
    public async Task InitialSetup_SeedsTheRolesAndPutsTheAdminInTheAdminRole()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var app = BuildIdentityApp(cs);

        Assert.IsType<OkObjectResult>(await InitialSetupAsync(app, "admin@example.io"));

        Assert.Equal(4L, await ScalarAsync<long>(cs, """SELECT COUNT(*) FROM "AspNetRoles" """));
        Assert.Equal(1L, await CountAdminsAsync(cs));
    }

    // ---- harness ------------------------------------------------------------

    /// <summary>Mirrors AuthService.HashToken — the plaintext is never stored.</summary>
    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<Guid> SeedRefreshTokenAsync(string cs, string tokenHash, Guid familyId)
    {
        await using var db = PostgresFixture.Context(cs);

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        return token.Id;
    }

    private static AuthService AuthService(ApplicationDbContext db)
    {
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        userManager.FindByIdAsync("u1").Returns(new ApplicationUser
        {
            Id = "u1",
            Email = "u1@example.io",
            UserName = "u1@example.io"
        });
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns(new List<string> { "Member" });

        var roleManager = Substitute.For<RoleManager<ApplicationRole>>(
            Substitute.For<IRoleStore<ApplicationRole>>(), null, null, null, null);

        var jwt = Substitute.For<IJwtTokenService>();
        jwt.GenerateAccessToken(Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<IList<Claim>>())
            .Returns("access-token");

        var settings = Options.Create(new JwtSettings
        {
            SecretKey = "test-secret-key-that-is-definitely-long-enough-0123456789abcdef",
            Issuer = "callu",
            Audience = "callu-clients",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        return new AuthService(
            userManager,
            roleManager,
            jwt,
            settings,
            Substitute.For<IHttpContextAccessor>(),
            new TransactionManager(db, NullLogger<TransactionManager>.Instance),
            new RefreshTokenRepository(db, NullLogger<RefreshTokenRepository>.Instance),
            Substitute.For<IAccessTokenRevocationStore>(),
            Substitute.For<IAuditLogService>(),
            NullLogger<AuthService>.Instance);
    }

    /// <summary>A real Identity stack over the container database, one connection per scope.</summary>
    private static ServiceProvider BuildIdentityApp(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.UseNodaTime();
            }));

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task<IActionResult> InitialSetupAsync(IServiceProvider app, string email)
    {
        using var scope = app.CreateScope();
        var sp = scope.ServiceProvider;

        var controller = new SetupController(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<ApplicationDbContext>(),
            new RoleSeeder(sp.GetRequiredService<RoleManager<ApplicationRole>>()),
            new SetupCompletionLatch(),
            NullLogger<SetupController>.Instance);

        return await controller.InitialSetup(
            new InitialSetupRequest(email, Password), CancellationToken.None);
    }

    /// <summary>Seeds only the four roles InitialSetup depends on, not the claims and templates.</summary>
    private sealed class RoleSeeder(RoleManager<ApplicationRole> roles) : IDbSeeder
    {
        public async Task SeedRolesAsync()
        {
            foreach (var role in new[] { "Admin", "TeamLead", "Member", "Viewer" })
                if (!await roles.RoleExistsAsync(role))
                    await roles.CreateAsync(new ApplicationRole { Name = role });
        }

        public Task SeedAsync() => SeedRolesAsync();
        public Task SeedRoleClaimsAsync() => Task.CompletedTask;
        public Task SeedDefaultSettingsAsync() => Task.CompletedTask;
    }

    private static Task<long> CountAdminsAsync(string cs) => ScalarAsync<long>(
        cs,
        """
        SELECT COUNT(*)
        FROM "AspNetUsers" u
        JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
        JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
        WHERE r."Name" = 'Admin'
        """);

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);

        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
