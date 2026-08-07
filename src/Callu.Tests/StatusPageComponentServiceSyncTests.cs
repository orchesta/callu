using System.Linq.Expressions;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Constants;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

public class ComponentStatusesFromServiceStatusTests
{
    [Theory]
    [InlineData(ServiceStatus.Operational, ComponentStatuses.Operational)]
    [InlineData(ServiceStatus.DegradedPerformance, ComponentStatuses.Degraded)]
    [InlineData(ServiceStatus.PartialOutage, ComponentStatuses.PartialOutage)]
    [InlineData(ServiceStatus.MajorOutage, ComponentStatuses.MajorOutage)]
    [InlineData(ServiceStatus.UnderMaintenance, ComponentStatuses.Maintenance)]
    public void FromServiceStatus_maps_each_catalog_status(ServiceStatus input, string expected)
        => Assert.Equal(expected, ComponentStatuses.FromServiceStatus(input));
}

public class StatusPageComponentServiceSyncTests
{
    private sealed class PassthroughTransactionManager : ITransactionManager
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
            => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
            => operation();

        public bool IsInTransaction() => false;
    }

    [Fact]
    public async Task SyncFromServiceStatuses_updates_linked_component_and_page_overall()
    {
        var serviceId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var component = new StatusPageComponent
        {
            Id = Guid.NewGuid(),
            Name = "API",
            StatusPageId = pageId,
            ServiceId = serviceId,
            Status = ComponentStatuses.Operational,
            HealthCheckEnabled = false
        };
        var page = new StatusPage
        {
            Id = pageId,
            Name = "Public",
            Slug = "public",
            OverallStatus = ComponentStatuses.Operational
        };

        var componentRepo = Substitute.For<IRepository<StatusPageComponent>>();
        componentRepo.FindAsync(Arg.Any<Expression<Func<StatusPageComponent, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var predicate = ci.Arg<Expression<Func<StatusPageComponent, bool>>>().Compile();
                var pool = new[] { component };
                return pool.Where(predicate).ToList().AsEnumerable();
            });

        var statusPageRepo = Substitute.For<IStatusPageRepository>();
        statusPageRepo.FindSingleAsync(Arg.Any<Expression<Func<StatusPage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var sut = new StatusPageComponentService(
            statusPageRepo,
            componentRepo,
            Substitute.For<IServiceRepository>(),
            Options.Create(new CommunicationSettingsOptions()),
            new PassthroughTransactionManager());

        await sut.SyncFromServiceStatusesAsync([(serviceId, ServiceStatus.MajorOutage)]);

        Assert.Equal(ComponentStatuses.MajorOutage, component.Status);
        Assert.Equal(ComponentStatuses.MajorOutage, page.OverallStatus);
    }

    [Fact]
    public async Task SyncFromServiceStatuses_skips_health_check_owned_components()
    {
        var serviceId = Guid.NewGuid();
        var component = new StatusPageComponent
        {
            Id = Guid.NewGuid(),
            Name = "Probed",
            StatusPageId = Guid.NewGuid(),
            ServiceId = serviceId,
            Status = ComponentStatuses.Operational,
            HealthCheckEnabled = true
        };

        var componentRepo = Substitute.For<IRepository<StatusPageComponent>>();
        componentRepo.FindAsync(Arg.Any<Expression<Func<StatusPageComponent, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var predicate = ci.Arg<Expression<Func<StatusPageComponent, bool>>>().Compile();
                return new[] { component }.Where(predicate).ToList().AsEnumerable();
            });

        var sut = new StatusPageComponentService(
            Substitute.For<IStatusPageRepository>(),
            componentRepo,
            Substitute.For<IServiceRepository>(),
            Options.Create(new CommunicationSettingsOptions()),
            new PassthroughTransactionManager());

        await sut.SyncFromServiceStatusesAsync([(serviceId, ServiceStatus.MajorOutage)]);

        Assert.Equal(ComponentStatuses.Operational, component.Status);
    }

    [Fact]
    public async Task SyncFromServiceStatuses_mirrors_recovery_to_operational()
    {
        var serviceId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var component = new StatusPageComponent
        {
            Id = Guid.NewGuid(),
            Name = "API",
            StatusPageId = pageId,
            ServiceId = serviceId,
            Status = ComponentStatuses.MajorOutage,
            HealthCheckEnabled = false
        };
        var page = new StatusPage
        {
            Id = pageId,
            Name = "Public",
            Slug = "public",
            OverallStatus = ComponentStatuses.MajorOutage
        };

        var componentRepo = Substitute.For<IRepository<StatusPageComponent>>();
        componentRepo.FindAsync(Arg.Any<Expression<Func<StatusPageComponent, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var predicate = ci.Arg<Expression<Func<StatusPageComponent, bool>>>().Compile();
                return new[] { component }.Where(predicate).ToList().AsEnumerable();
            });

        var statusPageRepo = Substitute.For<IStatusPageRepository>();
        statusPageRepo.FindSingleAsync(Arg.Any<Expression<Func<StatusPage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var sut = new StatusPageComponentService(
            statusPageRepo,
            componentRepo,
            Substitute.For<IServiceRepository>(),
            Options.Create(new CommunicationSettingsOptions()),
            new PassthroughTransactionManager());

        await sut.SyncFromServiceStatusesAsync([(serviceId, ServiceStatus.Operational)]);

        Assert.Equal(ComponentStatuses.Operational, component.Status);
        Assert.Equal(ComponentStatuses.Operational, page.OverallStatus);
    }
}
