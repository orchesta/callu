using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using System.Text.Json.Nodes;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Incidents;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A note is its own resource; the incident it belongs to is named as related, so the incident's
/// trail still reaches every row about it.</summary>
// Keyed by the note's id, a row says which note it is about without quoting the note. The incident's
// trail asks the search to follow related resources, so "everything that happened on this incident"
// still answers with the notes.
[Collection(PostgresCollection.Name)]
public class IncidentNoteAuditTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AddingANote_WritesARowKeyedToTheNote()
    {
        var world = await ArrangeAsync();

        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "Rolled back the deploy" }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Equal("IncidentNote", row.ResourceType);
        Assert.Equal(note.Id, row.ResourceId);
        Assert.Equal("ada", row.ActorId);
        Assert.Equal("Rolled back the deploy", row.ChangeAfter);
    }

    /// <summary>The row names the incident as related and carries its state, says which fields the note was
    /// created with, and never says what the note said.</summary>
    // The text stays in the row's change column, so the keyed chain seals it; the public event names the fields only.
    [PostgresFact]
    public async Task AddingANote_RelatesTheIncidentAsItStands_AndExportsNoNoteText()
    {
        var world = await ArrangeAsync();

        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "Rolled back the deploy" }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);
        var details = JsonNode.Parse(row.Details!)!;

        Assert.Equal("incident-note.create", row.EventName);
        Assert.Equal("incident-management", row.EventCategory);
        AssertRelatesTheIncident(details, world.IncidentId);
        Assert.Equal("open", details["metadata"]!["incident"]!["status"]!.GetValue<string>());
        Assert.Equal("critical", details["metadata"]!["incident"]!["priority"]!.GetValue<string>());
        Assert.Equal("create", details["change"]!["type"]!.GetValue<string>());
        Assert.Equal(["content", "isInternal"], ChangedFields(details));
        Assert.Null(details["change"]!["after"]);
        Assert.Equal("Rolled back the deploy", row.ChangeAfter);
        Assert.DoesNotContain("Rolled back", row.Details!, StringComparison.Ordinal);

        var sealedRow = await SealAsync(world.ConnectionString, row.Id);
        var oamEvent = JsonNode.Parse(sealedRow.OamEvent!)!;

        Assert.Equal("incident-note", oamEvent["resource"]!["type"]!.GetValue<string>());
        Assert.Equal(note.Id.ToString("D"), oamEvent["resource"]!["id"]!.GetValue<string>());
        AssertRelatesTheIncident(oamEvent, world.IncidentId);
        Assert.Equal(["content", "isInternal"], ChangedFields(oamEvent));
        Assert.DoesNotContain("Rolled back", sealedRow.OamEvent!, StringComparison.Ordinal);
    }

    /// <summary>The summary says what happened and nothing more: the key already names the note, and the text
    /// belongs in the change column alone.</summary>
    [PostgresFact]
    public async Task TheSummary_NamesNeitherTheNoteNorWhatItSaid()
    {
        var world = await ArrangeAsync();

        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "Rolled back the deploy" }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Equal("Note added", row.Summary);
        Assert.DoesNotContain(note.Id.ToString(), row.Summary!, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task AnInternalNote_IsMarkedAsSuchInTheTrail()
    {
        var world = await ArrangeAsync();

        await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "n", IsInternal = true }, "ada");

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Created);

        Assert.Equal("Note added (internal)", row.Summary);
    }

    /// <summary>Rewriting a note keeps both versions under the note; the exported change names the field, not the words.</summary>
    [PostgresFact]
    public async Task EditingANote_KeepsBothVersionsUnderTheNote_AndNamesTheFieldOnly()
    {
        var world = await ArrangeAsync();
        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "first" }, "ada");

        await world.Notes.UpdateNoteAsync(note.Id, new UpdateIncidentNoteRequest { Content = "second" });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Updated);
        var details = JsonNode.Parse(row.Details!)!;

        Assert.Equal("IncidentNote", row.ResourceType);
        Assert.Equal(note.Id, row.ResourceId);
        Assert.Equal("incident-note.update", row.EventName);
        Assert.Equal("Note edited", row.Summary);
        Assert.Equal("first", row.ChangeBefore);
        Assert.Equal("second", row.ChangeAfter);
        AssertRelatesTheIncident(details, world.IncidentId);
        Assert.Equal("update", details["change"]!["type"]!.GetValue<string>());
        Assert.Equal(["content"], ChangedFields(details));
        Assert.DoesNotContain("first", row.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("second", row.Details!, StringComparison.Ordinal);
    }

    /// <summary>Pinning alone is still an edit, and the row says that the pin is all it touched.</summary>
    [PostgresFact]
    public async Task PinningANote_RecordsThePinAsTheOnlyChange()
    {
        var world = await ArrangeAsync();
        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "keep" }, "ada");

        await world.Notes.UpdateNoteAsync(note.Id, new UpdateIncidentNoteRequest { Content = "keep", IsPinned = true });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Updated);

        Assert.Equal(["isPinned"], ChangedFields(JsonNode.Parse(row.Details!)!));
    }

    /// <summary>Deleting the note must not delete what it said, and must not export it either.</summary>
    [PostgresFact]
    public async Task DeletingANote_PreservesItsContentUnderTheNote_AndExportsNoneOfIt()
    {
        var world = await ArrangeAsync();
        var note = await world.Notes.AddNoteAsync(world.IncidentId, new CreateIncidentNoteRequest { Content = "the reason we failed over" }, "ada");

        await world.Notes.DeleteNoteAsync(note.Id);

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Deleted);
        var details = JsonNode.Parse(row.Details!)!;

        Assert.Equal("IncidentNote", row.ResourceType);
        Assert.Equal(note.Id, row.ResourceId);
        Assert.Equal("incident-note.delete", row.EventName);
        Assert.Equal("Note deleted", row.Summary);
        Assert.Equal("the reason we failed over", row.ChangeBefore);
        AssertRelatesTheIncident(details, world.IncidentId);
        Assert.Equal("delete", details["change"]!["type"]!.GetValue<string>());
        Assert.Null(details["change"]!["changedFields"]);
        Assert.DoesNotContain("failed over", row.Details!, StringComparison.Ordinal);
    }

    private static void AssertRelatesTheIncident(JsonNode document, Guid incidentId) =>
        Assert.Contains(document["relatedResources"]!.AsArray(), r =>
            r!["type"]!.GetValue<string>() == "incident" && r["id"]!.GetValue<string>() == incidentId.ToString("D"));

    private static IEnumerable<string> ChangedFields(JsonNode document) =>
        document["change"]!["changedFields"]!.AsArray().Select(f => f!.GetValue<string>());

    private static async Task<AuditLog> SealAsync(string connectionString, Guid rowId)
    {
        await using (var db = PostgresFixture.Context(connectionString))
            await new AuditChainService(db, DataProtectionProvider.Create(nameof(IncidentNoteAuditTests)),
                NullLogger<AuditChainService>.Instance).SealAsync();
        await using var reader = PostgresFixture.Context(connectionString);
        return await reader.AuditLogs.AsNoTracking().SingleAsync(l => l.Id == rowId);
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
