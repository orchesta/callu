using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>The preview runs the same parser as live ingest, saved template or not.</summary>
public class WebhookTemplatePreviewTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly WebhookTemplateService _sut;

    public WebhookTemplatePreviewTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"preview-{Guid.NewGuid():N}").Options);
        _sut = new WebhookTemplateService(
            new WebhookTemplateRepository(_ctx, NullLogger<WebhookTemplateRepository>.Instance),
            new WebhookCaptureRepository(_ctx, NullLogger<WebhookCaptureRepository>.Instance),
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new IntegrationRepository(_ctx, NullLogger<IntegrationRepository>.Instance),
            new SavingTransactionManager(_ctx),
            new WebhookPayloadParser(NullLogger<WebhookPayloadParser>.Instance));
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public void ValidMappings_ComeBackWithTheExtractedFields()
    {
        var result = _sut.PreviewTemplate(new PreviewWebhookTemplateRequest
        {
            SamplePayload = """{"alert":{"name":"disk full","sev":"critical"}}""",
            FieldMappings = """{"title":"$.alert.name","severity":"$.alert.sev"}"""
        });

        Assert.True(result.Success);
        Assert.NotNull(result.MappedFields);
        Assert.Equal("disk full", result.MappedFields!["title"]);
    }

    [Fact]
    public void AMappingThatFindsNoTitle_FailsWithAMessage()
    {
        var result = _sut.PreviewTemplate(new PreviewWebhookTemplateRequest
        {
            SamplePayload = """{"other":"thing"}""",
            FieldMappings = """{"title":"$.missing"}"""
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void StateDetection_MatchesTheBackend_OnlyExactResolvedValueResolves()
    {
        var result = _sut.PreviewTemplate(new PreviewWebhookTemplateRequest
        {
            SamplePayload = """{"title":"t","state":"weird"}""",
            FieldMappings = """{"title":"$.title"}""",
            StateMapping = """{"stateField":"$.state","openValue":"firing","resolvedValue":"resolved"}"""
        });

        Assert.True(result.Success);
        Assert.Equal("Open", result.MappedFields!["state"]);
    }
}
