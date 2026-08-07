using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Voximplant;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A responder taking an incident by pressing a key has to reach the audit trail.</summary>
// It is the most consequential act in the product — it stops every page going out — and it used to
// leave nothing behind but a timeline row, attributed to an editable display name.
[Collection(PostgresCollection.Name)]
public class PhoneKeypressAuditTests(PostgresFixture pg)
{
    private const string Phone = "+905321234567";

    [PostgresFact]
    public async Task PressingOne_WritesAnAcknowledgedRowAttributedToTheMatchedUser()
    {
        var world = await ArrangeAsync();

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "acknowledged"), null, NoopCallbacks());

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);

        Assert.Equal("Incident", row.ResourceType);
        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal(world.UserId, row.ActorId);
        Assert.Equal("Status: Open", row.ChangeBefore);
        Assert.Equal("Status: Acknowledged", row.ChangeAfter);
        Assert.Contains("keypress", row.Summary ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The incident row has to hold an id, like every other writer puts there.</summary>
    // A display name is editable and not unique, so "who took this incident" would be answerable
    // only by a string that the person themselves can change afterwards.
    [PostgresFact]
    public async Task PressingOne_RecordsTheAcknowledgerAsAUserId_NotADisplayName()
    {
        var world = await ArrangeAsync();

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "acknowledged"), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId);

        Assert.Equal(world.UserId, incident.AcknowledgedBy);
    }

    /// <summary>Two people sharing a phone number means the actor is unknown, not arbitrary.</summary>
    [PostgresFact]
    public async Task WhenThePhoneMatchesTwoUsers_TheRowCarriesNoActorRatherThanTheWrongOne()
    {
        var world = await ArrangeAsync(secondUserOnTheSameNumber: true);

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "acknowledged"), null, NoopCallbacks());

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);

        Assert.True(string.IsNullOrEmpty(row.ActorId),
            $"the acknowledgement was attributed to '{row.ActorId}' though two users share the number");
    }

    /// <summary>Starting a conference acknowledges the incident too, and that was silent.</summary>
    [PostgresFact]
    public async Task StartingAConference_WritesTheImplicitAcknowledgement()
    {
        var world = await ArrangeAsync();

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "conference_created"), null, NoopCallbacks());

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);

        Assert.Contains("conference", row.Summary ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A keypress on an incident somebody already took still stands down the retries.</summary>
    [PostgresFact]
    public async Task PressingOne_OnAnAlreadyAcknowledgedIncident_IsStillRecorded()
    {
        var world = await ArrangeAsync(status: IncidentStatus.Investigating);

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "acknowledged"), null, NoopCallbacks());

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);

        Assert.Contains("Investigating", row.ChangeAfter ?? "", StringComparison.Ordinal);
    }

    /// <summary>A call that merely rang changed nothing, so it writes nothing.</summary>
    [PostgresFact]
    public async Task ACallThatOnlyRang_WritesNoAuditRow()
    {
        var world = await ArrangeAsync();

        await world.Persistence.ProcessAsync(Callback(world.IncidentId, "s1", "alerting"), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<AuditLog>().AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------- harness

    private static async Task<AuditLog> SingleAuditRowAsync(string connectionString, AuditAction action)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == action)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync();

        Assert.True(rows.Count == 1,
            $"expected exactly one {action} row, found {rows.Count}");
        return rows[0];
    }

    private static VoxCallbackRequest Callback(Guid incidentId, string session, string status) => new()
    {
        IncidentId = incidentId.ToString(),
        CallSessionId = session,
        Status = status,
        Duration = 12,
        Data = new Dictionary<string, object> { ["phone"] = Phone },
    };

    private static VoximplantCallbackProcessingCallbacks NoopCallbacks() => new(
        (_, _) => Task.FromResult<VoxCallData?>(null),
        (_, _) => Task.FromResult(true),
        _ => Task.CompletedTask);

    private sealed record World(
        string ConnectionString, Guid IncidentId, string UserId, VoximplantVoiceCallbackPersistence Persistence);

    private async Task<World> ArrangeAsync(
        IncidentStatus status = IncidentStatus.Open,
        bool secondUserOnTheSameNumber = false)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        string userId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var responder = new ApplicationUser
            {
                Id = "responder-1",
                UserName = "ada",
                Email = "ada@example.com",
                PhoneNumber = Phone,
                FirstName = "Ada",
                LastName = "Çelik",
            };
            db.Users.Add(responder);
            userId = responder.Id;

            if (secondUserOnTheSameNumber)
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = "responder-2",
                    UserName = "mert",
                    Email = "mert@example.com",
                    PhoneNumber = Phone,
                });
            }

            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = status,
                StartedAt = DateTime.UtcNow,
                AcknowledgedAt = status == IncidentStatus.Open ? null : DateTime.UtcNow,
                AcknowledgedBy = status == IncidentStatus.Open ? null : "someone-else",
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        // The callback resolves this out of its own scope; without it the write is swallowed by the
        // catch that keeps a failing audit sink from breaking a live call.
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        var provider = services.BuildServiceProvider();

        var persistence = new VoximplantVoiceCallbackPersistence(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            provider,
            NullLogger<VoximplantVoiceCallbackPersistence>.Instance);

        return new World(cs, incidentId, userId, persistence);
    }
}
