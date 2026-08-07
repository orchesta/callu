using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Incidents;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A note is part of the incident's written record, so its trail hangs off the incident.</summary>
// Filed under the note's own id, the rows are unreachable from "show me everything that happened
// on this incident" — which is the only question an auditor asks.
[Collection(PostgresCollection.Name)]
public class IncidentNoteAuditTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AddingANote_WritesARowKeyedToTheIncident()
    {
        var world = await ArrangeAsync();

        await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "Rolled back the deploy" }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Equal("Incident", row.ResourceType);
        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal("ada", row.ActorId);
        Assert.Equal("Rolled back the deploy", row.ChangeAfter);
    }

    /// <summary>The note's own id is still recoverable, just not as the key.</summary>
    [PostgresFact]
    public async Task TheRow_NamesTheNoteItIsAbout()
    {
        var world = await ArrangeAsync();

        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "n" }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Contains(note.Id.ToString(), row.Summary ?? "", StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task AnInternalNote_IsMarkedAsSuchInTheTrail()
    {
        var world = await ArrangeAsync();

        await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "n", IsInternal = true }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Contains("internal", row.Summary ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rewriting a note keeps both versions, and the row reaches the incident.</summary>
    [PostgresFact]
    public async Task EditingANote_KeepsBothVersionsUnderTheIncident()
    {
        var world = await ArrangeAsync();
        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "first" }, "ada");

        await world.Notes.UpdateNoteAsync(note.Id, new UpdateIncidentNoteRequest { Content = "second" });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Updated);

        Assert.Equal("Incident", row.ResourceType);
        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal("first", row.ChangeBefore);
        Assert.Equal("second", row.ChangeAfter);
    }

    /// <summary>Deleting the note must not delete what it said.</summary>
    [PostgresFact]
    public async Task DeletingANote_PreservesItsContentUnderTheIncident()
    {
        var world = await ArrangeAsync();
        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "the reason we failed over" }, "ada");

        await world.Notes.DeleteNoteAsync(note.Id);

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Deleted);

        Assert.Equal("Incident", row.ResourceType);
        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal("the reason we failed over", row.ChangeBefore);
    }

    private static async Task<AuditLog> SingleRowAsync(string connectionString, AuditAction action)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == action).ToListAsync();

        Assert.True(rows.Count == 1, $"expected one {action} row, found {rows.Count}");
        return rows[0];
    }

    private sealed record World(string ConnectionString, Guid IncidentId, IIncidentNoteService Notes);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("ada");
        currentUser.IsInRole("Admin").Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(currentUser);
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;

        var notes = new IncidentNoteService(
            sp.GetRequiredService<IIncidentNoteRepository>(),
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            currentUser,
            sp.GetRequiredService<IAuditLogService>(),
            NullLogger<IncidentNoteService>.Instance);

        return new World(cs, incidentId, notes);
    }
}
