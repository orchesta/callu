using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Callu.Api.Controllers;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Audit;
using Callu.Tests.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The JSON Lines export seals first and writes the events stored at seal time.</summary>
[Collection(PostgresCollection.Name)]
public class AuditJsonLinesExportTests(PostgresFixture fixture)
{
    private sealed record World(AuditLogService Service, ApplicationDbContext Ctx, AuditLogsController Controller, HttpContext Http);

    private async Task<World> ArrangeAsync(IOpenAuditSigner? signer = null)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "auditor-1")], "test")),
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
        http.Response.Body = new MemoryStream();

        var service = new AuditLogService(
            new AuditLogRepository(ctx, NullLogger<AuditLogRepository>.Instance),
            new HttpContextAccessor { HttpContext = http },
            new TransactionManager(ctx, NullLogger<TransactionManager>.Instance));

        var chain = new AuditChainService(
            ctx,
            DataProtectionProvider.Create(nameof(AuditJsonLinesExportTests)),
            NullLogger<AuditChainService>.Instance);

        var signingKeys = Substitute.For<IAuditSigningKeyService>();
        signingKeys.GetActiveSignerAsync(Arg.Any<CancellationToken>()).Returns(signer);

        var controller = new AuditLogsController(service, chain, signingKeys,
            new AuditCheckpointService(ctx, signingKeys, Substitute.For<IHttpClientFactory>(),
                Options.Create(new AuditCheckpointOptions()), NullLogger<AuditCheckpointService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return new World(service, ctx, controller, http);
    }

    [PostgresFact]
    public async Task TheExport_SealsFirst_AndWritesTheStoredEventsInSequenceOrder()
    {
        var world = await ArrangeAsync();
        for (var i = 0; i < 3; i++)
            await world.Service.LogAsync("user-1", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());

        await world.Controller.Export(new AuditLogFilter(), "jsonl");

        var lines = Lines(world);
        var stored = await world.Ctx.AuditLogs.AsNoTracking().Where(l => l.OamEvent != null)
            .OrderBy(l => l.Sequence).Select(l => l.OamEvent!).ToListAsync();

        Assert.Equal(3, lines.Count);
        Assert.Equal(stored.Take(3), lines);
    }

    [PostgresFact]
    public async Task TheExportRow_NamesTheHeadTheFileWasSealedThrough()
    {
        var world = await ArrangeAsync();
        await world.Service.LogAsync("user-1", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());

        await world.Controller.Export(new AuditLogFilter(), "jsonl");

        var exportRow = await world.Ctx.AuditLogs.SingleAsync(l => l.Action == AuditAction.Exported);
        var last = JsonNode.Parse(Lines(world)[^1])!["sequence"]!.GetValue<long>();
        Assert.Contains($"sealedThrough={last}", exportRow.ChangeAfter);
        Assert.Null(exportRow.OamHash);
    }

    /// <summary>The first export after an upgrade does not wait for the whole backfill: it seals a bounded
    /// number of batches, writes what is sealed, and the Worker carries on with the rest.</summary>
    [PostgresFact]
    public async Task AnExport_SealsABoundedNumberOfBatches_AndWritesTheSealedPrefix()
    {
        var world = await ArrangeAsync();
        var cap = AuditLogsController.ExportSealBatches * AuditChainService.SealBatchSize;
        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        world.Ctx.AuditLogs.AddRange(Enumerable.Range(0, cap + 25).Select(i => new Callu.Domain.Entities.AuditLog
        {
            Id = Guid.NewGuid(),
            CreatedAt = start.AddSeconds(i),
            UpdatedAt = start.AddSeconds(i),
            ActorId = "user-1",
            ActorType = AuditActorType.User,
            Action = AuditAction.Viewed,
            EventName = "incident.view",
            EventCategory = "incident-management",
            Outcome = AuditOutcome.Success,
            ResourceType = "Incident",
            ResourceId = Guid.NewGuid(),
        }));
        await world.Ctx.SaveChangesAsync();
        world.Ctx.ChangeTracker.Clear();

        await world.Controller.Export(new AuditLogFilter(), "jsonl");

        var lines = Lines(world);
        Assert.Equal(cap, lines.Count);
        Assert.Equal(cap, JsonNode.Parse(lines[^1])!["sequence"]!.GetValue<long>());
        var exportRow = await world.Ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.Action == AuditAction.Exported);
        Assert.Contains($"sealedThrough={cap};", exportRow.ChangeAfter + ";");
        Assert.Equal(26, await world.Ctx.AuditLogs.CountAsync(l => l.OamHash == null));
    }

    [PostgresFact]
    public async Task ASelection_KeepsEveryEventVerifiable()
    {
        var world = await ArrangeAsync();
        await world.Service.LogAsync("user-1", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());
        await world.Service.LogAsync("user-2", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());
        await world.Service.LogAsync("user-1", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());

        await world.Controller.Export(new AuditLogFilter { ActorId = "user-1" }, "jsonl");

        var lines = Lines(world).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, e => Assert.Equal(e["integrity"]!["hash"]!.GetValue<string>(), OpenAuditDigest.Compute(e)));
        Assert.Contains("actorId=user-1", (await world.Ctx.AuditLogs.SingleAsync(l => l.Action == AuditAction.Exported)).ChangeAfter);
    }

    [PostgresFact]
    public async Task WithAnActiveKey_EveryLineIsSigned_OverItsDigestInput()
    {
        var world = await ArrangeAsync(signer: new Ed25519Signer(OamFixtures.TestKeyId, OamFixtures.TestSeed));
        await world.Service.LogAsync("user-1", AuditAction.Viewed, "Incident", Guid.NewGuid().ToString());

        await world.Controller.Export(new AuditLogFilter(), "jsonl");

        var line = JsonNode.Parse(Lines(world)[0])!.AsObject();
        var signature = line["integrity"]!["signature"]!;
        var publicKey = Convert.FromBase64String("kb7Bleqo+EYvJ2QYmWKh38+AmntzJMwdzMTFaf5hqyI=");
        Assert.True(Ed25519Signer.Verify(publicKey, OpenAuditDigest.Input(line), signature["value"]!.GetValue<string>()));
    }

    private static List<string> Lines(World world)
    {
        world.Http.Response.Body.Position = 0;
        return new StreamReader(world.Http.Response.Body).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
