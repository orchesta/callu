using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Services.Models;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Incidents;
using Callu.Shared.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Integration-style coverage of inbound-webhook ingestion, the product's primary untrusted surface.</summary>
public class WebhookProcessingServiceTests : IDisposable
{
    private static readonly Guid CreatedIncidentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ApplicationDbContext _ctx;
    private readonly IIncidentService _incidentService = Substitute.For<IIncidentService>();
    private readonly IWebhookPayloadParser _parser = Substitute.For<IWebhookPayloadParser>();
    private readonly IWebhookSignatureVerifier _verifier = Substitute.For<IWebhookSignatureVerifier>();
    private readonly WebhookProcessingService _sut;

    public WebhookProcessingServiceTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"webhook-{Guid.NewGuid():N}").Options);

        _incidentService.CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentCreateResult { Outcome = IncidentCreateOutcome.Created, Incident = new IncidentDto { Id = CreatedIncidentId } });

        _sut = new WebhookProcessingService(
            new WebhookCaptureRepository(_ctx, NullLogger<WebhookCaptureRepository>.Instance),
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new IntegrationRepository(_ctx, NullLogger<IntegrationRepository>.Instance),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new SavingTransactionManager(_ctx),
            _incidentService,
            _parser,
            _verifier,
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<WebhookProcessingService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    private Service SeedService(
        string token = "tok",
        string? apiKey = "the-api-key",
        string? secret = null,
        string? sigHeader = null,
        bool listening = false,
        bool withTemplate = true,
        string? providerId = "prometheus")
    {
        WebhookTemplate? template = null;
        if (withTemplate)
        {
            template = new WebhookTemplate
            {
                Id = Guid.NewGuid(),
                Name = "tmpl",
                FieldMappings = "{}",
                StateMapping = "{}",
                DataLanguage = "en-US",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };
            _ctx.Add(template);
        }

        var service = new Service
        {
            Id = Guid.NewGuid(),
            Name = "Payments API",
            ProviderId = providerId,
            WebhookToken = token,
            WebhookApiKey = apiKey,
            WebhookSecret = secret,
            WebhookSignatureHeader = sigHeader,
            WebhookListeningMode = listening,
            WebhookTemplate = template,
            WebhookTemplateId = template?.Id,
            WebhooksReceivedCount = 0,
            LastWebhookReceivedAt = null,
            TeamId = Guid.NewGuid(),
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(service);
        _ctx.SaveChanges();
        return service;
    }

    private void ParserReturns(bool success = true, WebhookState state = WebhookState.Open,
        string? externalId = "ext-1", string? title = "Database down", IncidentSeverity severity = IncidentSeverity.High, string? error = null)
    {
        _parser.Parse(Arg.Any<string>(), Arg.Any<WebhookTemplate>()).Returns(new ParsedWebhookPayload
        {
            Success = success,
            State = state,
            ExternalId = externalId,
            Title = title,
            Description = "details",
            Severity = severity,
            Error = error
        });
    }

    private Incident SeedIncident(Guid serviceId, IncidentStatus status, string? externalId, string title, DateTime createdAt)
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId,
            Status = status,
            ExternalAlertId = externalId,
            Title = title,
            Severity = IncidentSeverity.High,
            CreatedAt = createdAt,
            StartedAt = createdAt,
            IsDeleted = false
        };
        _ctx.Add(incident);
        _ctx.SaveChanges();
        return incident;
    }

    private Task<WebhookProcessResult> Process(string token, string? apiKey,
        IDictionary<string, string>? headers = null, string body = "{}")
        => _sut.ProcessWebhookAsync(token, apiKey, "POST", "application/json", body,
            headers ?? new Dictionary<string, string>(), "203.0.113.7");

    private Task<Incident> ReloadIncidentAsync(Guid id) =>
        _ctx.Incidents.AsNoTracking().IgnoreQueryFilters().FirstAsync(i => i.Id == id);

    private Task<Service> ReloadServiceAsync(Guid id) =>
        _ctx.Services.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == id);

    [Fact]
    public async Task UnknownToken_Rejected_NoIncidentCreated()
    {
        var result = await Process("nope", "x");
        Assert.False(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WebhookDisabled_Rejected()
    {
        var svc = SeedService(providerId: null);
        var result = await Process(svc.WebhookToken!, "the-api-key");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ApiKeyNotConfigured_Rejected()
    {
        var svc = SeedService(apiKey: null);
        var result = await Process(svc.WebhookToken!, "anything");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ApiKeyMissingInRequest_Rejected()
    {
        var svc = SeedService(apiKey: "the-api-key");
        var result = await Process(svc.WebhookToken!, apiKey: null);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task WrongApiKey_Rejected()
    {
        var svc = SeedService(apiKey: "the-api-key");
        var result = await Process(svc.WebhookToken!, "WRONG-key-value");
        Assert.False(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignatureConfiguredAndValid_CreatesIncident()
    {
        var svc = SeedService(secret: "shh", sigHeader: "X-Signature");
        _verifier.Verify(Arg.Any<string>(), "shh", Arg.Any<IDictionary<string, string>>(), "X-Signature").Returns(true);
        ParserReturns();

        var result = await Process(svc.WebhookToken!, "the-api-key",
            new Dictionary<string, string> { ["X-Signature"] = "sha256=deadbeef" });

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
    }

    [Fact]
    public async Task SignatureConfiguredButInvalid_Rejected_NoIncident()
    {
        var svc = SeedService(secret: "shh", sigHeader: "X-Signature");
        _verifier.Verify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<string>()).Returns(false);

        var result = await Process(svc.WebhookToken!, "the-api-key",
            new Dictionary<string, string> { ["X-Signature"] = "bad" });

        Assert.False(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListeningMode_CapturesPayload_NoParseNoIncident()
    {
        var svc = SeedService(listening: true);

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.True(result.WasCaptured);
        Assert.NotNull(result.CaptureId);
        Assert.True(await _ctx.WebhookCaptures.AnyAsync(c => c.ServiceId == svc.Id));
        _parser.DidNotReceive().Parse(Arg.Any<string>(), Arg.Any<WebhookTemplate>());
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewAlert_CreatesIncident_AndIncrementsStats()
    {
        var svc = SeedService();
        ParserReturns(externalId: "ext-new");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
        await _incidentService.Received(1).CreateIncidentAsync(
            Arg.Is<CreateIncidentRequest>(r => r.ExternalAlertId == "ext-new" && r.ServiceId == svc.Id),
            Arg.Any<CancellationToken>());
        var reloaded = await ReloadServiceAsync(svc.Id);
        Assert.Equal(1, reloaded.WebhooksReceivedCount);
        Assert.NotNull(reloaded.LastWebhookReceivedAt);
    }

    [Fact]
    public async Task PrimaryDedup_MatchingOpenIncident_NotRecreated()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: "ext-dup", title: "x", createdAt: DateTime.UtcNow);
        ParserReturns(externalId: "ext-dup");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Null(result.IncidentId);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimaryDedup_MatchingIncidentResolved_CreatesNew()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Resolved, externalId: "ext-r", title: "x", createdAt: DateTime.UtcNow);
        ParserReturns(externalId: "ext-r");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
    }

    [Fact]
    public async Task FuzzyDedup_NoExternalId_SameTitleWithinWindow_NotRecreated()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: null, title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-3));
        ParserReturns(externalId: null, title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Null(result.IncidentId);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FuzzyDedup_OutsideFiveMinuteWindow_CreatesNew()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: null, title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-10));
        ParserReturns(externalId: null, title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
    }

    /// <summary>
    /// A monitor that mints a fresh event id per firing is still fuzzy-deduped by title.
    /// </summary>
    [Fact]
    public async Task FuzzyDedup_UnmatchedExternalId_SameTitleWithinWindow_NotRecreated()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-001", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-2));
        ParserReturns(externalId: "evt-002", title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Null(result.IncidentId);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Several incidents can match the fuzzy window; the oldest is the one the rest duplicate.</summary>
    // The newer row is seeded first, which is the order an unordered read hands back.
    [Fact]
    public async Task FuzzyDedup_AdoptsOntoTheOldestMatch_NotAnArbitraryOne()
    {
        var svc = SeedService();
        var newer = SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-new", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-1));
        var older = SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-old", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-4));
        ParserReturns(externalId: "evt-003", title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());

        Assert.Equal("evt-003", (await ReloadIncidentAsync(older.Id)).ExternalAlertId);
        Assert.Equal("evt-new", (await ReloadIncidentAsync(newer.Id)).ExternalAlertId);
    }

    /// <summary>An id a live incident already holds is answered as a duplicate before any adoption.</summary>
    // This is what keeps the fuzzy branch from ever taking an id out from under a sibling, so the
    // adoption below it needs no conflict handling of its own.
    [Fact]
    public async Task ExternalIdHeldByALiveIncident_ShortCircuitsBeforeFuzzyAdoption()
    {
        var svc = SeedService();
        var titleMatch = SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-001", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-2));
        var holder = SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-002", title: "Disk full", createdAt: DateTime.UtcNow.AddMinutes(-2));
        ParserReturns(externalId: "evt-002", title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal("evt-001", (await ReloadIncidentAsync(titleMatch.Id)).ExternalAlertId);
        Assert.Equal("evt-002", (await ReloadIncidentAsync(holder.Id)).ExternalAlertId);
    }

    [Fact]
    public async Task FuzzyDedup_UnmatchedExternalId_DifferentTitle_CreatesNew()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-001", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-2));
        ParserReturns(externalId: "evt-002", title: "Disk full");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
    }

    [Fact]
    public async Task FuzzyDedup_UnmatchedExternalId_OutsideWindow_CreatesNew()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: "evt-001", title: "Rate limited", createdAt: DateTime.UtcNow.AddMinutes(-10));
        ParserReturns(externalId: "evt-002", title: "Rate limited");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
    }

    [Fact]
    public async Task SuppressedByMaintenanceWindow_SuccessWithNullIncidentId()
    {
        var svc = SeedService();
        ParserReturns();
        _incidentService.CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentCreateResult { Outcome = IncidentCreateOutcome.Suppressed, Reason = "Active maintenance" });

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Null(result.IncidentId);
        Assert.Contains("suppressed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolvedState_MatchingIncident_Resolves()
    {
        var svc = SeedService();
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: "ext-res", title: "x", createdAt: DateTime.UtcNow);
        ParserReturns(state: WebhookState.Resolved, externalId: "ext-res");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.NotNull(result.IncidentId);
        await _incidentService.Received(1).ResolveIncidentAsync(Arg.Any<Guid>(), "system:webhook", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvedState_NoMatchingIncident_NoResolveCall()
    {
        var svc = SeedService();
        ParserReturns(state: WebhookState.Resolved, externalId: "ext-none");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        await _incidentService.DidNotReceive().ResolveIncidentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TemplateParseFails_Rejected()
    {
        var svc = SeedService();
        ParserReturns(success: false, error: "no title");

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.False(result.Success);
        Assert.Contains("parsing failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EarlyRejection_DoesNotIncrementStats()
    {
        var svc = SeedService(apiKey: "the-api-key");
        await Process(svc.WebhookToken!, "WRONG");

        var reloaded = await ReloadServiceAsync(svc.Id);
        Assert.Equal(0, reloaded.WebhooksReceivedCount);
        Assert.Null(reloaded.LastWebhookReceivedAt);
    }

    // On the ingest path, clip; never reject — a 4xx is not retried, so the alert is simply lost.
    [Fact]
    public async Task AnOverLongAlertTitle_IsClippedToTheColumn_NotRejected()
    {
        var svc = SeedService();
        ParserReturns(
            title: new string('t', Incident.MaxTitleLength + 120),
            externalId: new string('e', Incident.MaxExternalAlertIdLength + 40));

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);

        var request = Assert.IsType<CreateIncidentRequest>(_incidentService.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IIncidentService.CreateIncidentAsync))
            .GetArguments()[0]);

        Assert.Equal(Incident.MaxTitleLength, request.Title.Length);
        Assert.Equal(Incident.MaxExternalAlertIdLength, request.ExternalAlertId!.Length);
    }

    [Fact]
    public async Task AnOverLongTitle_StillDedupesAgainstTheStoredClippedIncident()
    {
        var svc = SeedService();
        var clipped = new string('t', Incident.MaxTitleLength);
        SeedIncident(svc.Id, IncidentStatus.Open, externalId: null, title: clipped, createdAt: DateTime.UtcNow);

        ParserReturns(title: clipped + "and then some more summary text", externalId: null);

        var result = await Process(svc.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.DoesNotContain(_incidentService.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(IIncidentService.CreateIncidentAsync));
    }

    [Fact]
    public void Clip_NeverSplitsASurrogatePair()
    {
        var clipped = WebhookProcessingService.Clip(new string('a', Incident.MaxTitleLength - 1) + "😀", Incident.MaxTitleLength);

        Assert.NotNull(clipped);
        Assert.Equal(Incident.MaxTitleLength - 1, clipped!.Length);
        Assert.DoesNotContain(clipped, char.IsSurrogate);
    }

    private Integration SeedIntegration(
        string token = "int-tok",
        string? apiKey = "the-api-key",
        bool enabled = true,
        bool active = true,
        bool withTemplate = true)
    {
        var service = new Service
        {
            Id = Guid.NewGuid(),
            Name = "Payments API",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(service);

        WebhookTemplate? template = null;
        if (withTemplate)
        {
            template = new WebhookTemplate
            {
                Id = Guid.NewGuid(),
                Name = "grafana",
                FieldMappings = "{}",
                StateMapping = "{}",
                DataLanguage = "en-US",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };
            _ctx.Add(template);
        }

        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            Name = "Grafana",
            Type = IntegrationType.Grafana,
            ServiceId = service.Id,
            Service = service,
            WebhookToken = token,
            ApiKey = apiKey,
            WebhookEnabled = enabled,
            IsActive = active,
            WebhookTemplateId = template?.Id,
            WebhookTemplate = template,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(integration);
        _ctx.SaveChanges();
        return integration;
    }

    [Fact]
    public async Task IntegrationToken_CreatesIncident_WithSourceIntegrationId()
    {
        var integration = SeedIntegration();
        ParserReturns(externalId: "grafana-1");

        var result = await Process(integration.WebhookToken!, "the-api-key");

        Assert.True(result.Success);
        Assert.Equal(CreatedIncidentId, result.IncidentId);
        await _incidentService.Received(1).CreateIncidentAsync(
            Arg.Is<CreateIncidentRequest>(r =>
                r.ExternalAlertId == "grafana-1"
                && r.ServiceId == integration.ServiceId
                && r.SourceIntegrationId == integration.Id),
            Arg.Any<CancellationToken>());

        var reloaded = await _ctx.Integrations.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(i => i.Id == integration.Id);
        Assert.Equal(1, reloaded.WebhooksReceivedCount);
        Assert.NotNull(reloaded.LastWebhookReceivedAt);
    }

    [Fact]
    public async Task IntegrationToken_Disabled_IsRejected()
    {
        var integration = SeedIntegration(enabled: false);
        ParserReturns();

        var result = await Process(integration.WebhookToken!, "the-api-key");

        Assert.False(result.Success);
        await _incidentService.DidNotReceive().CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ServiceToken_StillLeaves_SourceIntegrationId_Null()
    {
        var svc = SeedService();
        ParserReturns(externalId: "svc-path");

        await Process(svc.WebhookToken!, "the-api-key");

        await _incidentService.Received(1).CreateIncidentAsync(
            Arg.Is<CreateIncidentRequest>(r => r.SourceIntegrationId == null && r.ServiceId == svc.Id),
            Arg.Any<CancellationToken>());
    }
}
