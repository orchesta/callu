using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Mapping;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

public class ServiceUpdatePartialPutTests : IDisposable
{
    static ServiceUpdatePartialPutTests() => new ServiceCollection().AddMappingConfig();

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceManagementService _sut;
    private readonly Guid _serviceId = Guid.NewGuid();

    public ServiceUpdatePartialPutTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"svc-partial-put-{Guid.NewGuid():N}").Options);

        _ctx.Services.Add(new Service
        {
            Id = _serviceId,
            Name = "checkout",
            TeamId = Guid.NewGuid(),
            IsPublic = false,
            AckEnabled = true,
            AckUrl = "https://monitoring.example/ack",
            AckHttpMethod = "PUT",
            AckContentType = "application/json",
            AckHeaders = """{"X-Api-Key":"k"}""",
            AckPayloadTemplate = """{"id": "{{incident.id}}"}""",
            CreatedAt = DateTime.UtcNow,
        });
        _ctx.SaveChanges();

        _sut = new ServiceManagementService(
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            Substitute.For<Callu.Application.Common.Interfaces.Persistence.IServiceDependencyRepository>(),
            new SavingTransactionManager(_ctx),
            Substitute.For<IAuditLogService>(),
            Substitute.For<IServiceStatusCascadeEngine>(),
            Substitute.For<IStatusPageComponentService>(),
            Substitute.For<IUptimeCalculator>(),
            Substitute.For<IIncidentService>(),
            Options.Create(new CommunicationSettingsOptions()),
            NullLogger<ServiceManagementService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    private Service Stored() => _ctx.Services.AsNoTracking().Single(s => s.Id == _serviceId);

    [Fact]
    public async Task ANameOnlyPut_LeavesTheAckConfigurationIntact()
    {
        var result = await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { Name = "renamed" });

        Assert.True(result.IsSuccess);
        var stored = Stored();
        Assert.Equal("renamed", stored.Name);
        Assert.True(stored.AckEnabled);
        Assert.Equal("https://monitoring.example/ack", stored.AckUrl);
        Assert.Equal("PUT", stored.AckHttpMethod);
        Assert.Equal("application/json", stored.AckContentType);
        Assert.Equal("""{"X-Api-Key":"k"}""", stored.AckHeaders);
        Assert.Equal("""{"id": "{{incident.id}}"}""", stored.AckPayloadTemplate);
    }

    [Fact]
    public async Task AnEmptyString_ClearsANullableAckField()
    {
        var result = await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { AckUrl = "" });

        Assert.True(result.IsSuccess);
        Assert.True(string.IsNullOrEmpty(Stored().AckUrl));
    }

    [Fact]
    public async Task AnOmittedAckEnabled_StaysOn_AndAnExplicitFalse_TurnsItOff()
    {
        await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { Name = "renamed" });
        Assert.True(Stored().AckEnabled);

        await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { AckEnabled = false });
        Assert.False(Stored().AckEnabled);
    }

    [Fact]
    public async Task AnOmittedTeamId_StillClearsTheTeam_TheUiClearsAssignmentByOmission()
    {
        var result = await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { Name = "renamed" });

        Assert.True(result.IsSuccess);
        Assert.Null(Stored().TeamId);
    }

    [Fact]
    public async Task AnOmittedIsPublic_NoLongerFlipsToFalse()
    {
        await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { IsPublic = true });
        Assert.True(Stored().IsPublic);

        await _sut.UpdateAsync(_serviceId, new UpdateServiceRequest { Name = "renamed" });
        Assert.True(Stored().IsPublic);
    }
}
