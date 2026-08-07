using Callu.Api.Controllers;
using Callu.Api.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Shared.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Where the base URL comes from on first run.</summary>
// The value ends up in every link Callu emails out. Taken from the Host header on each request it
// would be attacker-controlled behind a proxy, so it is captured once, here.
[Collection(PostgresCollection.Name)]
public class InitialSetupBaseUrlTests(PostgresFixture pg)
{
    private static UserManager<ApplicationUser> UserManagerThatSucceeds()
    {
        var manager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        manager.GetUsersInRoleAsync("Admin").Returns([]);
        manager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        manager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Admin").Returns(IdentityResult.Success);

        return manager;
    }

    private static SetupController Controller(ApplicationDbContext db, string host, string scheme = "https")
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = scheme;
        http.Request.Host = new HostString(host);

        return new SetupController(
            UserManagerThatSucceeds(),
            db,
            Substitute.For<IDbSeeder>(),
            new SetupCompletionLatch(),
            NullLogger<SetupController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        return cs;
    }

    private static async Task<string?> StoredBaseUrlAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return (await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync())?.BaseUrl;
    }

    [PostgresFact]
    public async Task InitialSetupWithBaseUrl_StoresItVerbatim()
    {
        var cs = await FreshDatabaseAsync();

        await using (var db = PostgresFixture.Context(cs))
        {
            var result = await Controller(db, "internal-hostname").InitialSetup(
                new InitialSetupRequest("admin@example.io", "Str0ng-Passw0rd!", BaseUrl: "https://callu.example.com"),
                CancellationToken.None);

            Assert.IsNotType<BadRequestObjectResult>(result);
        }

        Assert.Equal("https://callu.example.com", await StoredBaseUrlAsync(cs));
    }

    [PostgresFact]
    public async Task InitialSetupWithoutBaseUrl_FallsBackToRequestHost()
    {
        var cs = await FreshDatabaseAsync();

        await using (var db = PostgresFixture.Context(cs))
        {
            await Controller(db, "callu.example.com:8443").InitialSetup(
                new InitialSetupRequest("admin@example.io", "Str0ng-Passw0rd!"),
                CancellationToken.None);
        }

        Assert.Equal("https://callu.example.com:8443", await StoredBaseUrlAsync(cs));
    }

    // A trailing slash would double up against the paths appended to it.
    [PostgresFact]
    public async Task InitialSetupTrimsATrailingSlashFromTheSuppliedUrl()
    {
        var cs = await FreshDatabaseAsync();

        await using (var db = PostgresFixture.Context(cs))
        {
            await Controller(db, "internal-hostname").InitialSetup(
                new InitialSetupRequest("admin@example.io", "Str0ng-Passw0rd!", BaseUrl: "https://callu.example.com/  "),
                CancellationToken.None);
        }

        Assert.Equal("https://callu.example.com", await StoredBaseUrlAsync(cs));
    }

    /// <summary>An over-long Host header is clipped rather than made the reason setup fails.</summary>
    [PostgresFact]
    public async Task InitialSetupClipsADerivedUrlToTheColumn()
    {
        var cs = await FreshDatabaseAsync();
        var longHost = new string('a', OrganizationSettings.MaxBaseUrlLength) + ".example.com";

        await using (var db = PostgresFixture.Context(cs))
        {
            await Controller(db, longHost).InitialSetup(
                new InitialSetupRequest("admin@example.io", "Str0ng-Passw0rd!"),
                CancellationToken.None);
        }

        var stored = await StoredBaseUrlAsync(cs);
        Assert.NotNull(stored);
        Assert.Equal(OrganizationSettings.MaxBaseUrlLength, stored!.Length);
    }
}
