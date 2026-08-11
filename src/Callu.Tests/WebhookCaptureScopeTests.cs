using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>Capture listing and clearing stay inside their own endpoint's scope.</summary>
public class WebhookCaptureScopeTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly WebhookCaptureService _sut;

    public WebhookCaptureScopeTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"capture-scope-{Guid.NewGuid():N}").Options);
        _sut = new WebhookCaptureService(
            new WebhookCaptureRepository(_ctx, NullLogger<WebhookCaptureRepository>.Instance),
            new SavingTransactionManager(_ctx));
    }

    public void Dispose() => _ctx.Dispose();

    private Guid SeedCapture(Guid? serviceId = null, Guid? integrationId = null)
    {
        var capture = new WebhookCapture
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId,
            IntegrationId = integrationId,
            CapturedAt = DateTime.UtcNow,
            Body = "{}",
            Status = WebhookCaptureStatus.Captured,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(capture);
        _ctx.SaveChanges();
        return capture.Id;
    }

    [Fact]
    public async Task TheIntegrationList_ReturnsOnlyThatIntegrationsCaptures()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        SeedCapture(integrationId: mine);
        SeedCapture(integrationId: other);
        SeedCapture(serviceId: Guid.NewGuid());

        var captures = (await _sut.GetCapturesByIntegrationAsync(mine, 1, 50)).ToList();

        Assert.Single(captures);
        Assert.Equal(mine, captures[0].IntegrationId);
    }

    [Fact]
    public async Task ClearingOneIntegration_LeavesNeighboursAndServiceCapturesAlone()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        SeedCapture(integrationId: mine);
        SeedCapture(integrationId: mine);
        SeedCapture(integrationId: other);
        SeedCapture(serviceId: serviceId);

        var deleted = await _sut.DeleteAllCapturesByIntegrationAsync(mine);

        Assert.Equal(2, deleted);
        Assert.Empty(await _sut.GetCapturesByIntegrationAsync(mine, 1, 50));
        Assert.Single(await _sut.GetCapturesByIntegrationAsync(other, 1, 50));
        Assert.Single(await _sut.GetCapturesAsync(serviceId, 1, 50));
    }

    [Fact]
    public async Task ClearingAService_LeavesIntegrationCapturesAlone()
    {
        var serviceId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        SeedCapture(serviceId: serviceId);
        SeedCapture(integrationId: integrationId);

        var deleted = await _sut.DeleteAllCapturesAsync(serviceId);

        Assert.Equal(1, deleted);
        Assert.Single(await _sut.GetCapturesByIntegrationAsync(integrationId, 1, 50));
    }

    [Fact]
    public async Task TheServiceList_ExcludesCapturesThatArrivedThroughABoundIntegration()
    {
        var serviceId = Guid.NewGuid();
        SeedCapture(serviceId: serviceId);
        SeedCapture(serviceId: serviceId, integrationId: Guid.NewGuid());

        var captures = (await _sut.GetCapturesAsync(serviceId, 1, 50)).ToList();

        Assert.Single(captures);
        Assert.Null(captures[0].IntegrationId);
        Assert.Equal(1, await _sut.GetCaptureCountAsync(serviceId));
    }

    [Fact]
    public async Task ClearingAService_LeavesABoundIntegrationsCapturesAlone()
    {
        var serviceId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        SeedCapture(serviceId: serviceId);
        SeedCapture(serviceId: serviceId, integrationId: integrationId);

        var deleted = await _sut.DeleteAllCapturesAsync(serviceId);

        Assert.Equal(1, deleted);
        Assert.Single(await _sut.GetCapturesByIntegrationAsync(integrationId, 1, 50));
    }

    [Fact]
    public async Task DeletingACapture_RemovesTheRowPermanently()
    {
        var id = SeedCapture(serviceId: Guid.NewGuid());

        Assert.True(await _sut.DeleteCaptureAsync(id));

        Assert.Empty(_ctx.Set<WebhookCapture>().IgnoreQueryFilters().Where(c => c.Id == id));
        Assert.False(await _sut.DeleteCaptureAsync(id));
    }

    [Fact]
    public async Task Pagination_ClampsPageZeroAndOversizedPageSize()
    {
        var serviceId = Guid.NewGuid();
        for (var i = 0; i < 55; i++) SeedCapture(serviceId: serviceId);

        var oversized = (await _sut.GetCapturesAsync(serviceId, 0, 5000)).ToList();
        Assert.Equal(50, oversized.Count);

        var secondPage = (await _sut.GetCapturesAsync(serviceId, 2, 50)).ToList();
        Assert.Equal(5, secondPage.Count);
    }

    [Fact]
    public async Task ClearingAScope_RemovesTheRowsPermanently()
    {
        var integrationId = Guid.NewGuid();
        SeedCapture(integrationId: integrationId);
        SeedCapture(integrationId: integrationId);

        await _sut.DeleteAllCapturesByIntegrationAsync(integrationId);

        Assert.Empty(_ctx.Set<WebhookCapture>().IgnoreQueryFilters()
            .Where(c => c.IntegrationId == integrationId));
    }
}
