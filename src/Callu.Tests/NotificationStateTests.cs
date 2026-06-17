using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>
/// The Notification delivery state machine drives the retry queue. Locks in the exponential
/// backoff schedule (30s → 120s), the permanent-fail at MaxRetries, and the distinct
/// Skipped/PermanentlyFailed/Retrying states the queue scan relies on.
/// </summary>
public class NotificationStateTests
{
    [Fact]
    public void MarkDelivered_SetsSent_ClearsRetryAndError()
    {
        var n = new Notification { RetryCount = 1, NextRetryAt = DateTime.UtcNow, ErrorMessage = "x" };
        n.MarkDelivered();
        Assert.True(n.IsSent);
        Assert.NotNull(n.SentAt);
        Assert.Equal(NotificationDeliveryStatus.Delivered, n.DeliveryStatus);
        Assert.Null(n.NextRetryAt);
        Assert.Null(n.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_FirstAttempt_Failed_With30sBackoff()
    {
        var n = new Notification();
        var before = DateTime.UtcNow;
        n.MarkFailed("boom");

        Assert.Equal(1, n.RetryCount);
        Assert.Equal(NotificationDeliveryStatus.Failed, n.DeliveryStatus);
        Assert.Equal("boom", n.ErrorMessage);
        Assert.NotNull(n.NextRetryAt);
        Assert.InRange((n.NextRetryAt!.Value - before).TotalSeconds, 29, 35);
    }

    [Fact]
    public void MarkFailed_SecondAttempt_Failed_With120sBackoff()
    {
        var n = new Notification { RetryCount = 1 };
        var before = DateTime.UtcNow;
        n.MarkFailed("again");

        Assert.Equal(2, n.RetryCount);
        Assert.Equal(NotificationDeliveryStatus.Failed, n.DeliveryStatus);
        Assert.InRange((n.NextRetryAt!.Value - before).TotalSeconds, 119, 125);
    }

    [Fact]
    public void MarkFailed_AtMaxRetries_PermanentlyFailed_NoNextRetry()
    {
        var n = new Notification { RetryCount = Notification.MaxRetries - 1 };
        n.MarkFailed("final");

        Assert.Equal(Notification.MaxRetries, n.RetryCount);
        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, n.DeliveryStatus);
        Assert.Null(n.NextRetryAt);
    }

    [Fact]
    public void MarkPermanentlyFailed_TerminatesImmediately()
    {
        var n = new Notification { RetryCount = 0 };
        n.MarkPermanentlyFailed("user has no contact");
        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, n.DeliveryStatus);
        Assert.Null(n.NextRetryAt);
        Assert.Equal("user has no contact", n.ErrorMessage);
    }

    [Fact]
    public void MarkSending_IsSendingFirstTime_RetryingAfterFailure()
    {
        var fresh = new Notification { RetryCount = 0 };
        fresh.MarkSending();
        Assert.Equal(NotificationDeliveryStatus.Sending, fresh.DeliveryStatus);

        var retried = new Notification { RetryCount = 2 };
        retried.MarkSending();
        Assert.Equal(NotificationDeliveryStatus.Retrying, retried.DeliveryStatus);
    }

    [Fact]
    public void MarkSkipped_IsSkipped_NotRetried()
    {
        var n = new Notification();
        n.MarkSkipped("SMTP not configured");
        Assert.Equal(NotificationDeliveryStatus.Skipped, n.DeliveryStatus);
        Assert.Null(n.NextRetryAt);
        Assert.Equal("SMTP not configured", n.ErrorMessage);
    }
}
