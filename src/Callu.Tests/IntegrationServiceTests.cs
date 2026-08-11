using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class IntegrationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly IAuditLogService _audit = Substitute.For<IAuditLogService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IntegrationService _sut;
    private readonly Guid _serviceId = Guid.NewGuid();

    public IntegrationServiceTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"integration-{Guid.NewGuid():N}").Options);

        _ctx.Services.Add(new Service { Id = _serviceId, Name = "api", CreatedAt = DateTime.UtcNow });
        _ctx.SaveChanges();

        _currentUser.UserId.Returns("admin-1");

        _sut = new IntegrationService(
            new IntegrationRepository(_ctx, NullLogger<IntegrationRepository>.Instance),
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new WebhookTemplateRepository(_ctx, NullLogger<WebhookTemplateRepository>.Instance),
            new WebhookCaptureRepository(_ctx, NullLogger<WebhookCaptureRepository>.Instance),
            _audit,
            _currentUser,
            new SavingTransactionManager(_ctx),
            NullLogger<IntegrationService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task Create_MintsTokenAndApiKey_AndAudits()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Grafana",
            Type = "Grafana",
            ServiceId = _serviceId
        });

        Assert.NotEqual(Guid.Empty, secrets.Id);
        Assert.False(string.IsNullOrEmpty(secrets.WebhookToken));
        Assert.False(string.IsNullOrEmpty(secrets.ApiKey));
        Assert.Contains(secrets.WebhookToken, secrets.WebhookUrl);

        var stored = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == secrets.Id);
        Assert.Equal(secrets.WebhookToken, stored.WebhookToken);
        Assert.True(stored.WebhookEnabled);

        await _audit.Received(1).LogAsync(
            "admin-1", AuditAction.Created, "Integration", secrets.Id.ToString(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ClearsToken_SoIngressStops()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Grafana",
            Type = "Webhook",
            ServiceId = _serviceId
        });

        var deleted = await _sut.DeleteAsync(secrets.Id);

        Assert.True(deleted);
        var stored = await _ctx.Integrations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(i => i.Id == secrets.Id);
        Assert.True(stored.IsDeleted);
        Assert.Null(stored.WebhookToken);
        Assert.Null(stored.ApiKey);
        Assert.False(stored.WebhookEnabled);
    }

    [Fact]
    public async Task RotateCredentials_InvalidatesPreviousToken()
    {
        var created = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Datadog",
            Type = "DataDog",
            ServiceId = _serviceId
        });

        var rotated = await _sut.RotateCredentialsAsync(created.Id);

        Assert.NotEqual(created.WebhookToken, rotated.WebhookToken);
        Assert.NotEqual(created.ApiKey, rotated.ApiKey);
    }

    [Fact]
    public async Task AnIntegrationCanBeCreatedWithoutAService_AndDefaultsToListening()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Zabbix",
            Type = "Webhook"
        });

        var stored = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == secrets.Id);
        Assert.Null(stored.ServiceId);
        Assert.True(stored.ListeningMode);
    }

    [Fact]
    public async Task CreatingWithAService_DefaultsListeningOff()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Grafana",
            Type = "Grafana",
            ServiceId = _serviceId
        });

        var stored = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == secrets.Id);
        Assert.False(stored.ListeningMode);
    }

    [Fact]
    public async Task UpdateWithNullListeningMode_LeavesItUnchanged()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Zabbix",
            Type = "Webhook",
            ListeningMode = true,
            ServiceId = _serviceId
        });

        await _sut.UpdateAsync(secrets.Id, new UpdateIntegrationRequest
        {
            Name = "Zabbix",
            ListeningMode = null
        });

        var stored = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == secrets.Id);
        Assert.True(stored.ListeningMode);
    }

    [Fact]
    public async Task BindingToAMissingService_ThrowsNotFound()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest { Name = "Zabbix", Type = "Webhook" });

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.BindServiceAsync(secrets.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task BindingAService_SetsIt_AndUnbindingClearsIt()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest { Name = "Zabbix", Type = "Webhook" });

        var bound = await _sut.BindServiceAsync(secrets.Id, _serviceId);
        Assert.Equal(_serviceId, bound.ServiceId);

        var unbound = await _sut.BindServiceAsync(secrets.Id, null);
        Assert.Null(unbound.ServiceId);
    }

    [Fact]
    public async Task UpdateWithListeningModeSet_AppliesIt()
    {
        var secrets = await _sut.CreateAsync(new CreateIntegrationRequest
        {
            Name = "Zabbix",
            Type = "Webhook",
            ListeningMode = true,
            ServiceId = _serviceId
        });

        await _sut.UpdateAsync(secrets.Id, new UpdateIntegrationRequest
        {
            Name = "Zabbix",
            ListeningMode = false
        });

        var stored = await _ctx.Integrations.AsNoTracking().SingleAsync(i => i.Id == secrets.Id);
        Assert.False(stored.ListeningMode);
    }
}
