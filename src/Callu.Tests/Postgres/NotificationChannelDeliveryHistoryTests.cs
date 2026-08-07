using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A channel whose deliveries all failed must not look the same as one nobody has used.</summary>
[Collection(PostgresCollection.Name)]
public class NotificationChannelDeliveryHistoryTests(PostgresFixture pg)
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid OtherChannelId = Guid.NewGuid();

    [PostgresFact]
    public async Task History_IsScopedToTheChannelAndNewestFirst()
    {
        var cs = await SeededDatabaseAsync();
        await using var provider = BuildApp(cs);
        var service = NewService(provider);

        var page = await service.GetDeliveriesAsync(ChannelId, page: 1, pageSize: 50);

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, d => Assert.NotEqual(Guid.Empty, d.IncidentId));
        Assert.Equal(
            page.Items.Select(d => d.AttemptedAt).OrderByDescending(d => d).ToList(),
            page.Items.Select(d => d.AttemptedAt).ToList());
        Assert.Equal("Failed", page.Items[0].Status);
    }

    /// <summary>
    /// The page size is clamped in the service, not at the controller: this read is reachable by any
    /// role that can see settings, and the table grows with every incident on every channel.
    /// </summary>
    [PostgresFact]
    public async Task PageSizeAndPage_AreClampedByTheService()
    {
        var cs = await SeededDatabaseAsync();
        await using var provider = BuildApp(cs);
        var service = NewService(provider);

        var huge = await service.GetDeliveriesAsync(ChannelId, page: 1, pageSize: 100_000);
        Assert.Equal(100, huge.PageSize);

        var zero = await service.GetDeliveriesAsync(ChannelId, page: 0, pageSize: 0);
        Assert.Equal(1, zero.Page);
        Assert.Equal(1, zero.PageSize);
        Assert.Single(zero.Items);
    }

    /// <summary>The failure detail is what an operator came for; it has to survive the projection.</summary>
    [PostgresFact]
    public async Task AFailedDelivery_KeepsItsStatusCodeAndError()
    {
        var cs = await SeededDatabaseAsync();
        await using var provider = BuildApp(cs);
        var service = NewService(provider);

        var failed = (await service.GetDeliveriesAsync(ChannelId)).Items.First(d => d.Status == "Failed");

        Assert.Equal(404, failed.HttpStatus);
        Assert.Contains("no_service", failed.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(5, failed.AttemptCount);
    }

    /// <summary>"What did we actually send?" is unanswerable if the text stops at the projection.</summary>
    [PostgresFact]
    public async Task ADelivery_CarriesTheTextThatWasSent()
    {
        var cs = await SeededDatabaseAsync();
        await using var provider = BuildApp(cs);
        var service = NewService(provider);

        var delivery = (await service.GetDeliveriesAsync(ChannelId)).Items.First();

        Assert.Equal("🚨 [Critical] Incident: Payment API is returning 5xx", delivery.MessageText);
    }

    /// <summary>
    /// The card badge reads this. It has to come back with the channel list — one query, not one per
    /// channel — and it has to reflect the latest attempt even when that attempt failed.
    /// </summary>
    [PostgresFact]
    public async Task ChannelList_CarriesTheLatestOutcomePerChannel()
    {
        var cs = await SeededDatabaseAsync();
        await using var provider = BuildApp(cs);
        var service = NewService(provider);

        var channels = await service.GetAllAsync();

        var used = channels.Single(c => c.Id == ChannelId);
        Assert.Equal("Failed", used.LastDeliveryStatus);
        Assert.NotNull(used.LastDeliveryAt);

        var untouched = channels.Single(c => c.Id == OtherChannelId);
        Assert.Null(untouched.LastDeliveryStatus);
        Assert.Null(untouched.LastDeliveryAt);
    }

    private async Task<string> SeededDatabaseAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs);
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

        db.NotificationChannels.Add(Channel(ChannelId, "Slack — ops"));
        db.NotificationChannels.Add(Channel(OtherChannelId, "Teams — never used"));

        var baseTime = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);
        db.NotificationChannelDeliveries.AddRange(
            Delivery(baseTime, NotificationChannelDeliveryStatus.Succeeded, httpStatus: 200),
            Delivery(baseTime.AddMinutes(5), NotificationChannelDeliveryStatus.Retrying, httpStatus: 503),
            Delivery(baseTime.AddMinutes(10), NotificationChannelDeliveryStatus.Failed,
                httpStatus: 404, error: "HTTP 404: no_service", attempts: 5));

        await db.SaveChangesAsync();
        return cs;
    }

    private static NotificationChannel Channel(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        ChannelType = NotificationChannelType.Slack,
        ConfigurationJson = "{}",
        ServiceFilterJson = "[]",
        IsEnabled = true,
    };

    private static NotificationChannelDelivery Delivery(
        DateTime attemptedAt,
        NotificationChannelDeliveryStatus status,
        int? httpStatus = null,
        string? error = null,
        int attempts = 1) => new()
    {
        Id = Guid.NewGuid(),
        ChannelId = ChannelId,
        IncidentId = Guid.NewGuid(),
        EventKey = "incident.created",
        Title = "Payment API is returning 5xx",
        Severity = "Critical",
        MessageText = "🚨 [Critical] Incident: Payment API is returning 5xx",
        Status = status,
        HttpStatus = httpStatus,
        Error = error,
        AttemptCount = attempts,
        AttemptedAt = attemptedAt,
    };

    private static NotificationChannelService NewService(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        return new NotificationChannelService(
            scope.ServiceProvider.GetRequiredService<IRepository<NotificationChannel>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<NotificationChannelDelivery>>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IEmailService>(),
            new ProviderSecretProtector(
                new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance),
            Substitute.For<IOrganizationSettingsService>(),
            Options.Create(new CommunicationSettingsOptions()),
            Substitute.For<IAuditLogService>(),
            NullLogger<NotificationChannelService>.Instance);
    }

    private static ServiceProvider BuildApp(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        return services.BuildServiceProvider();
    }
}
