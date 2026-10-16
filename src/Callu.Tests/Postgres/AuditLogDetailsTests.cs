using System.Security.Claims;
using System.Text.Json.Nodes;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>What a caller passes in an AuditRecord reaches the row.</summary>
[Collection(PostgresCollection.Name)]
public class AuditLogDetailsTests(PostgresFixture fixture)
{
    private static AuditLogService Service(ApplicationDbContext ctx, HttpContext? http = null) =>
        new(new AuditLogRepository(ctx, NullLogger<AuditLogRepository>.Instance),
            new HttpContextAccessor { HttpContext = http },
            new TransactionManager(ctx, NullLogger<TransactionManager>.Instance));

    [PostgresFact]
    public async Task TheDetails_AreStored_AndTheStructuredChangeFillsTheDisplayColumns()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var ctx = PostgresFixture.Context(cs);

        await Service(ctx).LogAsync(new AuditRecord("user-1", AuditAction.Resolved, "Incident", Guid.NewGuid().ToString())
        {
            Details = new AuditDetails
            {
                Reason = new AuditReason(Text: "Disk temizlendi"),
                Change = new AuditChange("update", ["status"],
                    new JsonObject { ["status"] = "open" }, new JsonObject { ["status"] = "resolved" }),
            },
        });

        var row = await ctx.AuditLogs.SingleAsync();
        var details = JsonNode.Parse(row.Details!)!;
        Assert.Equal("Disk temizlendi", details["reason"]!["text"]!.GetValue<string>());
        Assert.Equal("""{"status":"open"}""", row.ChangeBefore);
        Assert.Equal("""{"status":"resolved"}""", row.ChangeAfter);
    }

    [PostgresFact]
    public async Task ARowWrittenInARequest_RecordsItsTemplateMethodAndAuthorization()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var ctx = PostgresFixture.Context(cs);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test")),
        };
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/incidents/2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10/resolve";
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v{version:apiVersion}/incidents/{id:guid}/resolve"), 0,
            new EndpointMetadataCollection(new AuthorizeAttribute { Policy = "CanResolveIncidents" }), "resolve"));

        await Service(ctx, http).LogAsync("user-1", AuditAction.Resolved, "Incident", Guid.NewGuid().ToString());

        var row = await ctx.AuditLogs.SingleAsync();
        Assert.Equal("/api/v{version}/incidents/{id}/resolve", row.RequestRoute);
        Assert.Equal("POST", row.RequestMethod);
        Assert.Equal(AuditActorType.User, row.ActorType);
        Assert.Equal("CanResolveIncidents", JsonNode.Parse(row.Details!)!["authorization"]!["policy"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task AWebhookRow_IsAllowedOnTheKey_AndIsASystemActor()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var ctx = PostgresFixture.Context(cs);

        using (AuditAuthorizationScope.Begin(AuditAuthorization.WebhookKey))
            await Service(ctx).LogAsync("system:webhook", AuditAction.Created, "Incident", Guid.NewGuid().ToString());

        var row = await ctx.AuditLogs.SingleAsync();
        Assert.Equal(AuditActorType.System, row.ActorType);
        Assert.Equal("service-webhook-key", JsonNode.Parse(row.Details!)!["authorization"]!["policy"]!.GetValue<string>());
    }
}
