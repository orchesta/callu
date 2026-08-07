using System.Net;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Push;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// Which device rows a prune actually deletes, against a real database.
/// </summary>
// The substitute-only harness can see that the prune path was entered and no further: the
// ExecuteUpdateAsync that picks the rows needs a relational provider to run at all, so a prune
// that deleted the wrong registrations would have looked identical there.
[Collection(PostgresCollection.Name)]
public class FcmPruneTests(PostgresFixture pg)
{
    private const string TokenUnregistered = """
        {"error":{"code":404,"status":"NOT_FOUND","message":"Requested entity was not found.",
        "details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError",
        "errorCode":"UNREGISTERED"}]}}
        """;

    private const string ProjectNotFound = """
        {"error":{"code":404,"status":"NOT_FOUND","message":"Requested entity was not found."}}
        """;

    [PostgresFact]
    public async Task ADeadTokenIsPruned_AndTheUsersOtherDevicesAreLeftAlone()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var dead = await SeedDeviceAsync(cs, "u1", "dead-token");
        var alive = await SeedDeviceAsync(cs, "u1", "live-token");
        var someoneElse = await SeedDeviceAsync(cs, "u2", "other-user-token");

        await SendAsync(cs, dead, HttpStatusCode.NotFound, TokenUnregistered);

        Assert.True(await IsDeletedAsync(cs, dead));
        Assert.False(await IsDeletedAsync(cs, alive));
        Assert.False(await IsDeletedAsync(cs, someoneElse));
    }

    /// <summary>A project-level 404 is about the configuration, so nobody's registration is touched.</summary>
    [PostgresFact]
    public async Task AProjectLevelFailure_DeletesNothing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var device = await SeedDeviceAsync(cs, "u1", "tok");

        await SendAsync(cs, device, HttpStatusCode.NotFound, ProjectNotFound);

        Assert.False(await IsDeletedAsync(cs, device));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedDeviceAsync(string cs, string userId, string token)
    {
        await using var db = PostgresFixture.Context(cs);
        var device = new UserPushDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Platform = "ios",
            PushToken = token,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }

    private static async Task<bool> IsDeletedAsync(string cs, Guid deviceId)
    {
        await using var db = PostgresFixture.Context(cs);
        var device = await db.UserPushDevices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == deviceId);
        return device.IsDeleted;
    }

    /// <summary>Sends to the one device under test, with a real context behind the prune scope.</summary>
    private static async Task SendAsync(string cs, Guid deviceId, HttpStatusCode status, string body)
    {
        await using var db = PostgresFixture.Context(cs);
        var device = await db.UserPushDevices.AsNoTracking().FirstAsync(d => d.Id == deviceId);

        var protector = new FirebaseCredentialProtector(
            new EphemeralDataProtectionProvider(), NullLogger<FirebaseCredentialProtector>.Instance);

        var settings = Substitute.For<IFirebaseSettingsRepository>();
        settings.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new FirebaseSettings
        {
            Id = FirebaseSettings.SingletonId,
            ProjectId = "demo-project",
            ServiceAccountJson = protector.Protect(FcmMobilePushSenderTests.ServiceAccountJson()),
            IsConfigured = true
        });

        var devices = Substitute.For<IUserPushDeviceRepository>();
        devices.GetActiveByUserIdAsync(device.UserId, Arg.Any<CancellationToken>()).Returns([device]);

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(cs, npgsql => npgsql.UseNodaTime()));
        await using var provider = services.BuildServiceProvider();

        var handler = new FcmMobilePushSenderTests.StubHandler(status, body);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("fcm").Returns(_ => new HttpClient(handler, disposeHandler: false));

        var sender = new FcmMobilePushSender(
            settings,
            devices,
            provider.GetRequiredService<IServiceScopeFactory>(),
            protector,
            new FcmAccessTokenProvider(factory, NullLogger<FcmAccessTokenProvider>.Instance),
            factory,
            NullLogger<FcmMobilePushSender>.Instance);

        await sender.SendToUserAsync(device.UserId, new NotificationItemDto
        {
            Id = Guid.NewGuid(),
            Title = "Incident opened",
            Message = "api-gateway is down"
        });
    }

}
