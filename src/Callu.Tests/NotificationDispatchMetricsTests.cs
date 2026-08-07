using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Telemetry;

namespace Callu.Tests;

/// <summary>
/// TEL-3: notification-failure metrics must tag a small, bounded "reason" — never the raw,
/// high-cardinality, potentially PII-bearing provider error text.
/// </summary>
public class NotificationDispatchMetricsTests
{
    private static Notification Failed(string? error) =>
        new() { DeliveryStatus = NotificationDeliveryStatus.Failed, ErrorMessage = error };

    private static readonly string[] AllowedReasons =
        ["permanent", "auth", "rate-limited", "timeout", "config", "transient", "unknown"];

    [Fact]
    public void PermanentlyFailed_Maps_To_Permanent()
    {
        var n = new Notification
        {
            DeliveryStatus = NotificationDeliveryStatus.PermanentlyFailed,
            ErrorMessage = "anything at all",
        };
        Assert.Equal("permanent", NotificationDispatchMetrics.ClassifyFailure(n));
    }

    [Theory]
    [InlineData("401 Unauthorized", "auth")]
    [InlineData("Request returned 403 Forbidden", "auth")]
    [InlineData("429 Too Many Requests", "rate-limited")]
    [InlineData("provider throttled the request", "rate-limited")]
    [InlineData("Connection timed out after 10s", "timeout")]
    [InlineData("SMTP not configured", "config")]
    [InlineData("Email notifications disabled org-wide", "config")]
    [InlineData("550 mailbox unavailable", "transient")]
    public void Maps_Failed_Message_To_Bounded_Reason(string error, string expected)
    {
        Assert.Equal(expected, NotificationDispatchMetrics.ClassifyFailure(Failed(error)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Error_Maps_To_Unknown(string? error)
    {
        Assert.Equal("unknown", NotificationDispatchMetrics.ClassifyFailure(Failed(error)));
    }

    [Fact]
    public void Tag_Never_Leaks_Pii_From_Error_Text()
    {
        var n = Failed("550 mailbox <victim@example.com> +15551234567 rejected");
        var reason = NotificationDispatchMetrics.ClassifyFailure(n);

        Assert.DoesNotContain("@", reason);
        Assert.DoesNotContain("5551234567", reason);
        Assert.Contains(reason, AllowedReasons);
    }
}
