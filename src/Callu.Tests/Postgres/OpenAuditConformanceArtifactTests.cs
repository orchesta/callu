using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Api;
using Callu.Api.Controllers;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Audit;
using Callu.Shared.Models.Incidents;
using Callu.Tests.Audit;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Org.BouncyCastle.Crypto.Parameters;

namespace Callu.Tests;

/// <summary>Writes an export, a checkpoint and a proof for CI to verify with OpenAuditModel's own tool.</summary>
// The test itself asserts only what callu can check; the verdict that matters is the reference tool's,
// which the backend job runs on these files right after this suite.
[Collection(PostgresCollection.Name)]
public class OpenAuditConformanceArtifactTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task WritesTheConformanceArtifacts()
    {
        var target = Environment.GetEnvironmentVariable("CALLU_OAM_ARTIFACTS");
        var world = await ArrangeAsync();

        const string resolveReason = "Bağlantı havuzu büyütüldü.";
        const string closeReason = "Kök neden giderildi; izleme 24 saat temiz kaldı.";
        const string reopenReason = "Aynı hata gece yeniden görüldü.";
        const string noteText = "Veritabanı bağlantıları izleniyor.";

        // A legacy-shaped row first, as a database upgraded from 1.2.0 holds.
        var legacyId = await world.LogLegacyRowAsync();

        var incident = await world.CreateIncidentAsync(IncidentSeverity.High);
        await world.AddNoteAsync(incident, noteText);
        await world.ResolveAsync(incident, "permanent-fix", resolveReason);
        await world.CloseAsync(incident, closeReason);
        await world.ReopenAsync(incident, reopenReason);
        await world.SettingsChangeAsync();

        await world.Chain.SealAsync();
        var checkpoint = await world.Checkpoints.IssueManualAsync("CI run: checkpoint kept with the build artifacts", default);
        await world.ExportAsync();

        var lines = world.ExportLines();
        var proven = lines.Select(l => JsonNode.Parse(l)!.AsObject())
            .First(e => e["event"]!["name"]!.GetValue<string>() == "incident.resolve");
        var proof = await world.Checkpoints.ProofAsync(Guid.Parse(proven["id"]!.GetValue<string>()), checkpoint!.Id, default);

        Assert.All(lines, l => OamFixtures.AssertValidEvent(JsonNode.Parse(l)!));
        Assert.Equal(AuditProofStatus.Found, proof.Status);

        // The gate is only as good as what the export holds: every shape the reference tool should see.
        var events = lines.Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        var ofIncident = events.Where(e => e["resource"]!["id"]!.GetValue<string>() == incident.ToString("D")).ToList();
        JsonObject Named(string name) => ofIncident.Single(e => e["event"]!["name"]!.GetValue<string>() == name
            && e["change"]?["changedFields"]?.AsArray().Any(f => f!.GetValue<string>() == "notes") != true);

        Assert.Contains(events, e => e["id"]!.GetValue<string>() == legacyId.ToString("D"));
        Assert.Equal(resolveReason, Named("incident.resolve")["reason"]!["text"]!.GetValue<string>());
        Assert.Equal(closeReason, Named("incident.close")["reason"]!["text"]!.GetValue<string>());
        Assert.Equal(reopenReason, Named("incident.reopen")["reason"]!["text"]!.GetValue<string>());
        Assert.Equal("high", Named("incident.create")["metadata"]!["incident"]!["priority"]!.GetValue<string>());
        var noteRow = Assert.Single(ofIncident, e => e["event"]!["name"]!.GetValue<string>() == "incident.create"
            && e["change"]?["changedFields"]?.AsArray().Any(f => f!.GetValue<string>() == "notes") == true);
        Assert.NotNull(noteRow["metadata"]!["incident"]!["status"]);
        Assert.DoesNotContain(lines, l => l.Contains(noteText, StringComparison.Ordinal));

        if (string.IsNullOrEmpty(target)) return;

        Directory.CreateDirectory(target);
        await File.WriteAllLinesAsync(Path.Combine(target, "export.jsonl"), lines);
        await File.WriteAllTextAsync(Path.Combine(target, "checkpoint.json"), checkpoint.Document);
        await File.WriteAllTextAsync(Path.Combine(target, "proof.json"), proof.Document!);
        await File.WriteAllTextAsync(Path.Combine(target, "event.json"), proven.ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(target, "public-key.pem"), Ed25519Signer.ToPem(world.PublicKey));
    }

    private sealed class World
    {
        public required IHttpContextAccessor Accessor { get; init; }
        public required ApplicationDbContext Ctx { get; init; }
        public required IAuditLogService Audit { get; init; }
        public required IIncidentService Incidents { get; init; }
        public required IIncidentNoteService Notes { get; init; }
        public required AuditChainService Chain { get; init; }
        public required AuditCheckpointService Checkpoints { get; init; }
        public required AuditLogsController Controller { get; init; }
        public required Guid TeamId { get; init; }
        public required byte[] PublicKey { get; init; }

        // Each call runs as the panel's request would: signed in, on the action's real route and policy.
        public void As(string userId, string pattern, string policy, string method = "POST") =>
            Accessor.HttpContext = Request(new DefaultHttpContext(), userId, pattern, policy, method);

        private static HttpContext Request(HttpContext http, string userId, string pattern, string policy, string method)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
            http.Request.Method = method;
            http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
                new EndpointMetadataCollection(new AuthorizeAttribute(), new AuthorizeAttribute { Policy = policy }), pattern));
            return http;
        }

        public async Task<Guid> LogLegacyRowAsync()
        {
            // What a 1.2.0 row looks like: no Details, a resolved route, a key=value change string.
            var id = Guid.NewGuid();
            Ctx.AuditLogs.Add(new AuditLog
            {
                Id = id,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
                ActorId = "admin-1",
                ActorType = AuditActorType.User,
                Action = AuditAction.SettingsChanged,
                EventName = "sip-trunk.settings-change",
                EventCategory = "platform-administration",
                Outcome = AuditOutcome.Success,
                ResourceType = "SipTrunk",
                ResourceId = Guid.NewGuid(),
                RequestRoute = "/api/v1/sip-trunks/2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10",
                ChangeAfter = "name=pbx; server=10.0.0.2:5060; user=pbx",
            });
            await Ctx.SaveChangesAsync();
            return id;
        }

        // The patterns and policies below are the ones the controller actions declare: IncidentsController
        // routes on [controller], so its template keeps the class name's casing.
        public async Task<Guid> CreateIncidentAsync(IncidentSeverity severity)
        {
            As("admin-1", "api/v{version:apiVersion}/Incidents", Policies.CanManageIncidents);
            var result = await Incidents.CreateIncidentAsync(new CreateIncidentRequest
            {
                Title = "Checkout latency",
                Severity = severity.ToString(),
                TeamId = TeamId,
            });
            return result.Incident!.Id;
        }

        public Task AddNoteAsync(Guid id, string content)
        {
            As("admin-1", "api/v{version:apiVersion}/Incidents/{id:guid}/notes", Policies.CanManageIncidents);
            return Notes.AddNoteAsync(id, new CreateIncidentNoteRequest { Content = content }, "admin-1");
        }

        public Task ResolveAsync(Guid id, string type, string reason)
        {
            As("admin-1", "api/v{version:apiVersion}/Incidents/{id:guid}/resolve", Policies.CanResolveIncidents);
            return Incidents.ResolveIncidentAsync(id, "admin-1", IncidentTransitionNote.FromInput(type, reason));
        }

        public Task CloseAsync(Guid id, string reason)
        {
            As("admin-1", "api/v{version:apiVersion}/Incidents/{id:guid}/close", Policies.CanResolveIncidents);
            return Incidents.CloseIncidentAsync(id, "admin-1", IncidentTransitionNote.FromInput(null, reason));
        }

        public Task ReopenAsync(Guid id, string reason)
        {
            As("admin-1", "api/v{version:apiVersion}/Incidents/{id:guid}/reopen", Policies.CanManageIncidents);
            return Incidents.ReopenIncidentAsync(id, "admin-1", IncidentTransitionNote.FromInput(null, reason));
        }

        public Task SettingsChangeAsync()
        {
            As("admin-1", "api/v{version:apiVersion}/providers/sip-trunks/{id:guid}", Policies.CanManageIntegrations, "PUT");
            return Audit.LogAsync("admin-1", AuditAction.SettingsChanged, "SipTrunk", Guid.NewGuid().ToString(),
                newValues: "name=pbx; server=10.0.0.2:5060; user=pbx");
        }

        public Task ExportAsync()
        {
            Accessor.HttpContext = Request(Controller.HttpContext, "auditor-1",
                "api/v{version:apiVersion}/audit-logs/export", Policies.CanViewAuditLog, "GET");
            return Controller.Export(new AuditLogFilter(), "jsonl");
        }

        public List<string> ExportLines()
        {
            var body = (MemoryStream)Controller.HttpContext.Response.Body;
            return Encoding.UTF8.GetString(body.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }

    private static IValidator<CreateIncidentRequest> PassingValidator()
    {
        var validator = Substitute.For<IValidator<CreateIncidentRequest>>();
        validator.ValidateAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return validator;
    }

    private async Task<World> ArrangeAsync()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid teamId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var team = new Team { Name = "Payments" };
            db.Add(team);
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();
        var sp = provider.CreateScope().ServiceProvider;
        var ctx = sp.GetRequiredService<ApplicationDbContext>();
        var audit = sp.GetRequiredService<IAuditLogService>();

        var incidents = new IncidentService(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            sp.GetRequiredService<ICallLogRepository>(),
            Substitute.For<IRepository<WebhookDelivery>>(),
            sp.GetRequiredService<IRepository<ConferenceRoom>>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            PassingValidator(),
            Substitute.For<IIncidentEventDispatcher>(),
            Substitute.For<IEscalationOrchestrator>(),
            Substitute.For<IEscalationWorkflowSignal>(),
            Substitute.For<IAlertRuleEngine>(),
            audit,
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IServiceRepository>(),
            Substitute.For<INotificationChannelService>(),
            Substitute.For<IMaintenanceWindowService>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<IncidentService>.Instance);

        var notes = new IncidentNoteService(
            sp.GetRequiredService<IIncidentNoteRepository>(),
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<ICurrentUserService>(),
            audit,
            NullLogger<IncidentNoteService>.Instance);

        // A fresh key per run: the files carry their own public key, so nothing is checked against a secret.
        var seed = RandomNumberGenerator.GetBytes(32);
        var signingKeys = Substitute.For<IAuditSigningKeyService>();
        signingKeys.GetActiveSignerAsync(Arg.Any<CancellationToken>()).Returns(new Ed25519Signer("callu-ci", seed));

        var chain = new AuditChainService(
            ctx, DataProtectionProvider.Create(nameof(OpenAuditConformanceArtifactTests)), NullLogger<AuditChainService>.Instance);
        var checkpoints = new AuditCheckpointService(ctx, signingKeys, Substitute.For<IHttpClientFactory>(),
            Options.Create(new AuditCheckpointOptions()), NullLogger<AuditCheckpointService>.Instance);

        var exportHttp = new DefaultHttpContext();
        exportHttp.Response.Body = new MemoryStream();
        var controller = new AuditLogsController(audit, chain, signingKeys, checkpoints)
        {
            ControllerContext = new ControllerContext { HttpContext = exportHttp },
        };

        return new World
        {
            Accessor = sp.GetRequiredService<IHttpContextAccessor>(),
            Ctx = ctx,
            Audit = audit,
            Incidents = incidents,
            Notes = notes,
            Chain = chain,
            Checkpoints = checkpoints,
            Controller = controller,
            TeamId = teamId,
            PublicKey = new Ed25519PrivateKeyParameters(seed).GeneratePublicKey().GetEncoded(),
        };
    }
}
