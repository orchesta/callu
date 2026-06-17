using Callu.Domain.Entities;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;

namespace Callu.Tests;

/// <summary>
/// SSRF-3: channel config validation must reject internal/loopback/metadata webhook URLs at
/// write time, not just at send time.
/// </summary>
public class NotificationChannelConfigurationGuardTests
{
    [Theory]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.0.0.5/")]
    public void Rejects_Internal_Slack_Webhook_Url(string url)
    {
        Assert.Throws<ValidationException>(() =>
            NotificationChannelConfigurationGuard.EnsureValid(
                NotificationChannelType.Slack,
                new Dictionary<string, string> { ["webhookUrl"] = url },
                notifyOnCreated: true, notifyOnAcknowledged: false, notifyOnResolved: false));
    }

    [Fact]
    public void Rejects_NonHttp_Scheme()
    {
        Assert.Throws<ValidationException>(() =>
            NotificationChannelConfigurationGuard.EnsureValid(
                NotificationChannelType.Webhook,
                new Dictionary<string, string> { ["url"] = "file:///etc/passwd" },
                notifyOnCreated: true, notifyOnAcknowledged: false, notifyOnResolved: false));
    }

    [Fact]
    public void Accepts_Public_Webhook_Url()
    {
        NotificationChannelConfigurationGuard.EnsureValid(
            NotificationChannelType.Webhook,
            new Dictionary<string, string> { ["url"] = "https://1.1.1.1/hook" },
            notifyOnCreated: true, notifyOnAcknowledged: false, notifyOnResolved: false);
    }
}
