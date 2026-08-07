using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// Slack/Teams webhookUrl and generic-webhook secret are encrypted at rest (enc:v1: sentinel),
/// masked in DTOs, and preserved across an unchanged (masked) update.
/// </summary>
public class NotificationChannelSecretProtectionTests
{
    // Literal public IP so IsValidHealthCheckUrl passes without a DNS query (hermetic offline).
    private const string SlackUrl = "https://8.8.8.8/services/T00/B00/abcd1234TOKEN";

    private static (NotificationChannelService svc, List<NotificationChannel> store) CreateService()
    {
        var store = new List<NotificationChannel>();
        var repo = Substitute.For<IRepository<NotificationChannel>>();
        repo.When(r => r.AddAsync(Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()))
            .Do(ci => store.Add(ci.Arg<NotificationChannel>()));
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => store.FirstOrDefault(c => c.Id == ci.Arg<Guid>()));

        var deliveryRepo = Substitute.For<IRepository<NotificationChannelDelivery>>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var email = Substitute.For<IEmailService>();
        var protector = new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance);

        var options = Options.Create(new CommunicationSettingsOptions());

        var svc = new NotificationChannelService(
            repo, deliveryRepo, uow, httpFactory, email, protector,
            Substitute.For<IOrganizationSettingsService>(), options,
            Substitute.For<IAuditLogService>(),
            NullLogger<NotificationChannelService>.Instance);
        return (svc, store);
    }

    private static CreateNotificationChannelRequest SlackRequest() => new()
    {
        Name = "slack",
        ChannelType = "Slack",
        Configuration = new() { ["webhookUrl"] = SlackUrl },
        NotifyOnIncidentCreated = true,
    };

    [Fact]
    public async Task Create_encrypts_webhookUrl_at_rest()
    {
        var (svc, store) = CreateService();

        await svc.CreateAsync(SlackRequest());

        var stored = Assert.Single(store);
        Assert.Contains("enc:v1:", stored.ConfigurationJson);
        Assert.DoesNotContain("abcd1234TOKEN", stored.ConfigurationJson);
    }

    [Fact]
    public async Task Create_returns_masked_webhookUrl()
    {
        var (svc, _) = CreateService();

        var dto = await svc.CreateAsync(SlackRequest());

        var masked = dto.Configuration["webhookUrl"];
        Assert.StartsWith("••••", masked);
        Assert.EndsWith("OKEN", masked);
        Assert.DoesNotContain("abcd1234TOKEN", masked);
    }

    [Fact]
    public async Task Update_with_masked_value_preserves_secret()
    {
        var (svc, store) = CreateService();
        var dto = await svc.CreateAsync(SlackRequest());
        var masked = dto.Configuration["webhookUrl"];

        // Frontend re-sends the masked value on an unchanged edit — must not corrupt the secret,
        // and the guard (which requires a valid http URL) must not reject the mask.
        var ok = await svc.UpdateAsync(dto.Id, new UpdateNotificationChannelRequest
        {
            Name = "slack-renamed",
            Configuration = new() { ["webhookUrl"] = masked },
            NotifyOnIncidentCreated = true,
        });

        Assert.True(ok);
        Assert.DoesNotContain("••••", store.Single().ConfigurationJson);
        Assert.Contains("enc:v1:", store.Single().ConfigurationJson);

        var reread = await svc.GetByIdAsync(dto.Id);
        Assert.EndsWith("OKEN", reread!.Configuration["webhookUrl"]);
    }

    [Fact]
    public async Task Update_with_new_url_reencrypts()
    {
        var (svc, store) = CreateService();
        var dto = await svc.CreateAsync(SlackRequest());
        const string newUrl = "https://8.8.8.8/services/T11/B11/newSECRET99";

        await svc.UpdateAsync(dto.Id, new UpdateNotificationChannelRequest
        {
            Name = "slack",
            Configuration = new() { ["webhookUrl"] = newUrl },
            NotifyOnIncidentCreated = true,
        });

        Assert.DoesNotContain("newSECRET99", store.Single().ConfigurationJson);
        var reread = await svc.GetByIdAsync(dto.Id);
        Assert.EndsWith("ET99", reread!.Configuration["webhookUrl"]);
    }

    [Fact]
    public async Task Webhook_secret_encrypted_url_stays_plaintext()
    {
        var (svc, store) = CreateService();

        var dto = await svc.CreateAsync(new CreateNotificationChannelRequest
        {
            Name = "hook",
            ChannelType = "Webhook",
            Configuration = new() { ["url"] = "https://8.8.8.8/hook", ["secret"] = "mysecretvalue" },
            NotifyOnIncidentCreated = true,
        });

        var stored = store.Single();
        Assert.Contains("enc:v1:", stored.ConfigurationJson);
        Assert.Contains("8.8.8.8/hook", stored.ConfigurationJson);
        Assert.DoesNotContain("mysecretvalue", stored.ConfigurationJson);

        Assert.Equal("https://8.8.8.8/hook", dto.Configuration["url"]);
        Assert.StartsWith("••••", dto.Configuration["secret"]);
    }
}
