using System.Net;
using System.Security.Claims;
using Callu.Api.Controllers;
using Callu.Application.Services;
using NSubstitute;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>Taking the trail out of the product is itself audited, and the row says what left.</summary>
// A row reading only "somebody exported" cannot answer whose history went with the file.
[Collection(PostgresCollection.Name)]
public class AuditExportSelfAuditTests(PostgresFixture fixture)
{
    private sealed record World(AuditLogsController Controller, ApplicationDbContext Ctx, string ConnectionString);

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Callu.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private async Task<World> ArrangeAsync()
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
            DataProtectionProvider.Create(nameof(AuditExportSelfAuditTests)),
            NullLogger<AuditChainService>.Instance);

        var controller = new AuditLogsController(
            service, chain, Substitute.For<IAuditSigningKeyService>(), new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return new World(controller, ctx, cs);
    }

    // Paging and sort order say nothing about which rows the file holds.
    private static readonly string[] NotNarrowings = ["Page", "PageSize", "SortAscending"];

    [PostgresFact]
    public async Task TheExportRow_NamesEveryNarrowingTheFileWasTakenUnder()
    {
        var world = await ArrangeAsync();
        await using var _ctx = world.Ctx;
        var entity = Guid.NewGuid();

        await world.Controller.Export(new AuditLogFilter
        {
            ResourceTypes = ["Incident", "Escalation", "NotificationChannel"],
            ResourceId = entity,
            ActorId = "subject-user",
            RequestIpAddress = "203.0.113.4",
        });

        var values = (await SingleExportRowAsync(world.ConnectionString)).ChangeAfter ?? "";

        var named = values
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal);

        // A narrowing added to the filter and forgotten here leaves the row describing a file
        // whose contents it no longer accounts for.
        var missing = typeof(AuditLogFilter).GetProperties()
            .Where(p => !NotNarrowings.Contains(p.Name))
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Where(name => !named.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0, $"the export row does not name: {string.Join(", ", missing)}");

        Assert.Contains("resourceTypes=Incident|Escalation|NotificationChannel", values, StringComparison.Ordinal);
        Assert.Contains($"resourceId={entity}", values, StringComparison.Ordinal);
        Assert.Contains("actorId=subject-user", values, StringComparison.Ordinal);
        Assert.Contains("requestIpAddress=203.0.113.4", values, StringComparison.Ordinal);
    }

    /// <summary>Narrowed to one entity, the row is filed under that entity as well as describing it.</summary>
    [PostgresFact]
    public async Task TheExportRow_IsKeyedToTheEntityItWasNarrowedTo()
    {
        var world = await ArrangeAsync();
        await using var _ctx = world.Ctx;
        var entity = Guid.NewGuid();

        await world.Controller.Export(new AuditLogFilter { ResourceId = entity });

        var row = await SingleExportRowAsync(world.ConnectionString);

        Assert.Equal("AuditLog", row.ResourceType);
        Assert.Equal(entity, row.ResourceId);
        Assert.Equal("auditor-1", row.ActorId);
    }

    /// <summary>A whole-trail export has no one entity to name and must not invent one.</summary>
    [PostgresFact]
    public async Task AnUnnarrowedExport_LeavesTheEntityIdEmpty()
    {
        var world = await ArrangeAsync();
        await using var _ctx = world.Ctx;

        await world.Controller.Export(new AuditLogFilter());

        var row = await SingleExportRowAsync(world.ConnectionString);

        Assert.Null(row.ResourceId);
    }

    private static async Task<AuditLog> SingleExportRowAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == AuditAction.Exported)
            .ToListAsync();

        Assert.True(rows.Count == 1, $"expected one Exported row, found {rows.Count}");
        return rows[0];
    }
}
