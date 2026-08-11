using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>Creating a template from a capture attaches it to whichever endpoint the capture came through.</summary>
public class WebhookTemplateFromCaptureTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly WebhookTemplateService _sut;

    public WebhookTemplateFromCaptureTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"from-capture-{Guid.NewGuid():N}").Options);
        _sut = new WebhookTemplateService(
            new WebhookTemplateRepository(_ctx, NullLogger<WebhookTemplateRepository>.Instance),
            new WebhookCaptureRepository(_ctx, NullLogger<WebhookCaptureRepository>.Instance),
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new IntegrationRepository(_ctx, NullLogger<IntegrationRepository>.Instance),
            new SavingTransactionManager(_ctx),
            new WebhookPayloadParser(NullLogger<WebhookPayloadParser>.Instance));
    }

    public void Dispose() => _ctx.Dispose();

    private (Service service, Integration integration, WebhookCapture capture) Seed(
        bool captureOnIntegration, bool captureAlsoCarriesService = false)
    {
        var service = new Service { Id = Guid.NewGuid(), Name = "api", CreatedAt = DateTime.UtcNow };
        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            Name = "Zabbix",
            Type = IntegrationType.Webhook,
            CreatedAt = DateTime.UtcNow
        };
        var capture = new WebhookCapture
        {
            Id = Guid.NewGuid(),
            IntegrationId = captureOnIntegration ? integration.Id : null,
            ServiceId = captureOnIntegration
                ? (captureAlsoCarriesService ? service.Id : null)
                : service.Id,
            CapturedAt = DateTime.UtcNow,
            Body = """{"title":"disk full"}""",
            Status = WebhookCaptureStatus.Captured,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.AddRange(service, integration, capture);
        _ctx.SaveChanges();
        return (service, integration, capture);
    }

    private Task<WebhookTemplateDto> CreateFromCapture(Guid captureId) =>
        _sut.CreateTemplateFromCaptureAsync(captureId, new CreateWebhookTemplateRequest
        {
            Name = "zabbix-template",
            FieldMappings = """{"title":"$.title"}"""
        });

    [Fact]
    public async Task AnIntegrationCapture_BindsTheTemplateToTheIntegration()
    {
        var (service, integration, capture) = Seed(captureOnIntegration: true);

        var template = await CreateFromCapture(capture.Id);

        var storedIntegration = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id);
        var storedService = await _ctx.Services.AsNoTracking().SingleAsync(s => s.Id == service.Id);
        Assert.Equal(template.Id, storedIntegration.WebhookTemplateId);
        Assert.Null(storedService.WebhookTemplateId);
    }

    [Fact]
    public async Task AServiceCapture_StillRebindsTheService()
    {
        var (service, integration, capture) = Seed(captureOnIntegration: false);

        var template = await CreateFromCapture(capture.Id);

        var storedService = await _ctx.Services.AsNoTracking().SingleAsync(s => s.Id == service.Id);
        var storedIntegration = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id);
        Assert.Equal(template.Id, storedService.WebhookTemplateId);
        Assert.Null(storedIntegration.WebhookTemplateId);
    }

    [Fact]
    public async Task ACaptureCarryingBoth_BindsOnlyTheIntegration()
    {
        var (service, integration, capture) = Seed(captureOnIntegration: true, captureAlsoCarriesService: true);

        var template = await CreateFromCapture(capture.Id);

        var storedIntegration = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == integration.Id);
        var storedService = await _ctx.Services.AsNoTracking().SingleAsync(s => s.Id == service.Id);
        Assert.Equal(template.Id, storedIntegration.WebhookTemplateId);
        Assert.Null(storedService.WebhookTemplateId);
    }

    [Fact]
    public async Task TheCapture_IsMarkedUsedForTemplate()
    {
        var (_, _, capture) = Seed(captureOnIntegration: true);

        await CreateFromCapture(capture.Id);

        var stored = await _ctx.WebhookCaptures.AsNoTracking().SingleAsync(c => c.Id == capture.Id);
        Assert.Equal(WebhookCaptureStatus.UsedForTemplate, stored.Status);
    }
}
