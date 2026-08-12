using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.UnitOfWork;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

public class ServiceActionCrudTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly ServiceActionService _sut;
    private readonly Guid _serviceId = Guid.NewGuid();

    public ServiceActionCrudTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"svc-actions-{Guid.NewGuid():N}").Options);

        _ctx.Services.Add(new Service { Id = _serviceId, Name = "checkout", CreatedAt = DateTime.UtcNow });
        _ctx.SaveChanges();

        _sut = new ServiceActionService(
            new Repository<ServiceAction>(_ctx, NullLogger<Repository<ServiceAction>>.Instance),
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new UnitOfWork(_ctx, NullLoggerFactory.Instance),
            new SavingTransactionManager(_ctx),
            Substitute.For<IAuditLogService>(),
            Substitute.For<ICurrentUserService>(),
            Options.Create(new CommunicationSettingsOptions()),
            NullLogger<ServiceActionService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    private static CreateServiceActionRequest Create(string name = "Restart Redis", string url = "https://93.184.216.34/restart") =>
        new() { Name = name, Url = url, HttpMethod = "POST", Secret = "s3cret" };

    [Fact]
    public async Task CreatingAnAction_PersistsIt_AndTheDtoNeverCarriesTheSecret()
    {
        var dto = await _sut.CreateAsync(_serviceId, Create());

        Assert.True(dto.HasSecret);
        Assert.Equal("POST", dto.HttpMethod);
        Assert.DoesNotContain("s3cret", System.Text.Json.JsonSerializer.Serialize(dto));

        var stored = await _ctx.ServiceActions.AsNoTracking().SingleAsync(a => a.Id == dto.Id);
        Assert.Equal("s3cret", stored.Secret);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task ADuplicateName_IsA409_NotASecondButton()
    {
        await _sut.CreateAsync(_serviceId, Create());

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(_serviceId, Create()));
    }

    [Fact]
    public async Task TheCap_StopsCreationAtTheLimit()
    {
        for (var i = 0; i < ServiceAction.MaxPerService; i++)
            await _sut.CreateAsync(_serviceId, Create(name: $"action-{i}"));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(_serviceId, Create(name: "one-too-many")));
    }

    [Fact]
    public async Task AMissingService_IsA404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(Guid.NewGuid(), Create()));
    }

    [Fact]
    public async Task AnInternalUrl_IsRejectedAtSaveTime()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAsync(_serviceId, Create(url: "http://127.0.0.1/restart")));
    }

    [Fact]
    public async Task UpdateWithANullSecret_KeepsIt_AndAnEmptyStringClearsIt()
    {
        var dto = await _sut.CreateAsync(_serviceId, Create());

        var kept = await _sut.UpdateAsync(_serviceId, dto.Id, new UpdateServiceActionRequest { Name = "Renamed" });
        Assert.True(kept.HasSecret);

        var cleared = await _sut.UpdateAsync(_serviceId, dto.Id, new UpdateServiceActionRequest { Secret = "" });
        Assert.False(cleared.HasSecret);
    }

    [Fact]
    public async Task RenamingOntoAnExistingName_IsA409()
    {
        await _sut.CreateAsync(_serviceId, Create(name: "First"));
        var second = await _sut.CreateAsync(_serviceId, Create(name: "Second"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.UpdateAsync(_serviceId, second.Id, new UpdateServiceActionRequest { Name = "First" }));
    }

    [Fact]
    public async Task DeletingAnAction_SoftDeletes_AndTheListNoLongerShowsIt()
    {
        var dto = await _sut.CreateAsync(_serviceId, Create());

        await _sut.DeleteAsync(_serviceId, dto.Id);

        Assert.Empty(await _sut.GetForServiceAsync(_serviceId, includeSensitive: true));
        var stored = await _ctx.ServiceActions.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == dto.Id);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task AfterDeletion_TheNameIsFreeAgain()
    {
        var dto = await _sut.CreateAsync(_serviceId, Create());
        await _sut.DeleteAsync(_serviceId, dto.Id);

        var recreated = await _sut.CreateAsync(_serviceId, Create());
        Assert.NotEqual(dto.Id, recreated.Id);
    }

    [Fact]
    public async Task WithoutTheManageClaim_HeaderValuesAreWithheld()
    {
        await _sut.CreateAsync(_serviceId, Create() with { HeadersJson = """{"Authorization":"Bearer tok"}""" });

        var masked = await _sut.GetForServiceAsync(_serviceId, includeSensitive: false);
        Assert.Null(masked.Single().HeadersJson);

        var full = await _sut.GetForServiceAsync(_serviceId, includeSensitive: true);
        Assert.Contains("Authorization", full.Single().HeadersJson);
    }

    [Fact]
    public async Task TheList_IsOrderedByDisplayOrderThenName()
    {
        await _sut.CreateAsync(_serviceId, Create(name: "Zebra") with { DisplayOrder = 0 });
        await _sut.CreateAsync(_serviceId, Create(name: "Alpha") with { DisplayOrder = 1 });
        await _sut.CreateAsync(_serviceId, Create(name: "Mango") with { DisplayOrder = 0 });

        var list = await _sut.GetForServiceAsync(_serviceId, includeSensitive: true);

        Assert.Equal(new[] { "Mango", "Zebra", "Alpha" }, list.Select(a => a.Name));
    }
}
