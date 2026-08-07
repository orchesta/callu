using System.Diagnostics.Metrics;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Notifications;
using NodaTime;

namespace Callu.Tests;

/// <summary>
/// Pins <see cref="IClock"/> so wall-clock-sensitive behaviour (quiet hours, occurrence windows)
/// is deterministic. NodaTime.Testing isn't referenced — this is all we need from it.
/// </summary>
internal sealed class FixedClock(Instant now) : IClock
{
    public Instant GetCurrentInstant() => now;
}

/// <summary>The four answers a real channel dispatcher can come back with.</summary>
internal enum ChannelOutcome
{
    /// <summary>The provider took it.</summary>
    Delivered,

    /// <summary>No provider was available, so the row is deferred without spending retry budget.</summary>
    NoProvider,

    /// <summary>The provider was asked and said no. Retriable, and it costs one of three attempts.</summary>
    Failed,

    /// <summary>A data problem no retry can fix: the user has no phone number, no email address.</summary>
    Permanent,

    /// <summary>
    /// The channel is off for good — SMTP unconfigured, a provider that will never exist. Nothing was
    /// sent and nothing ever will be.
    /// </summary>
    Skipped
}

/// <summary>Records what it was asked to send and writes back the outcome a real dispatcher would.</summary>
internal sealed class RecordingChannelDispatcher(
    NotificationType channel,
    ChannelOutcome outcome = ChannelOutcome.Delivered) : INotificationChannelDispatcher
{
    public NotificationType Channel => channel;

    /// <summary>What the provider answers. Settable so one dispatcher can go cold and then recover.</summary>
    public ChannelOutcome Outcome { get; set; } = outcome;

    /// <summary>The rows this channel was ASKED to deliver — not necessarily the rows that went out.</summary>
    public List<Notification> Sent { get; } = [];

    /// <summary>The rows this channel was asked to RE-send (the retry queue's drain), in order.</summary>
    public List<Notification> Retried { get; } = [];

    /// <summary>The users a "send test notification" reached.</summary>
    public List<string> TestsSent { get; } = [];

    /// <summary>Makes the re-send blow up, so the queue drain's own failure handling is exercised.</summary>
    public Exception? RetryThrows { get; set; }

    public Task SendAsync(
        Notification notification,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default)
    {
        Sent.Add(notification);
        RecordOutcome(notification);
        return Task.CompletedTask;
    }

    public Task RetryAsync(
        Notification notification,
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        Retried.Add(notification);
        if (RetryThrows is not null) throw RetryThrows;

        Sent.Add(notification);
        RecordOutcome(notification);
        return Task.CompletedTask;
    }

    public Task<(bool Success, string Message)> SendTestAsync(
        string userId,
        string? email,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        TestsSent.Add(userId);

        (bool Success, string Message) answer = Outcome == ChannelOutcome.Delivered
            ? (true, "ok")
            : (false, Reason);

        return Task.FromResult(answer);
    }

    private string Reason => Outcome switch
    {
        ChannelOutcome.NoProvider => "no provider is registered for this channel",
        ChannelOutcome.Failed => "the provider refused the send",
        ChannelOutcome.Permanent => "the user has no contact on this channel",
        ChannelOutcome.Skipped => "this channel is not configured",
        _ => "ok"
    };

    /// <summary>The same four calls the real dispatchers make on the row.</summary>
    private void RecordOutcome(Notification notification)
    {
        switch (Outcome)
        {
            case ChannelOutcome.Delivered:
                notification.MarkDelivered();
                break;
            case ChannelOutcome.NoProvider:
                notification.MarkDeferred(DateTime.UtcNow.AddSeconds(60), Reason);
                break;
            case ChannelOutcome.Failed:
                notification.MarkFailed(Reason);
                break;
            case ChannelOutcome.Permanent:
                notification.MarkPermanentlyFailed(Reason);
                break;
            case ChannelOutcome.Skipped:
                notification.MarkSkipped(Reason);
                break;
        }
    }
}

/// <summary>The real-time push channel as a double, succeeding on every call the way SignalR does.</summary>
internal sealed class RecordingPushService : INotificationPushService
{
    /// <summary>The users a notification was pushed to, in order.</summary>
    public List<string> Pushed { get; } = [];

    /// <summary>Makes the hub blow up, so the caller's "push unavailable" branch is actually driven.</summary>
    public Exception? Throws { get; set; }

    public Task PushNotificationAsync(string userId, NotificationItemDto notification, CancellationToken cancellationToken = default)
    {
        Pushed.Add(userId);
        if (Throws is not null) throw Throws;
        return Task.CompletedTask;
    }

    public Task PushUnreadCountAsync(string userId, int count, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task BroadcastIncidentUpdateAsync(Guid incidentId, string status, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task BroadcastServiceUpdatedAsync(Guid serviceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task BroadcastTeamUpdatedAsync(Guid teamId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task BroadcastScheduleUpdatedAsync(Guid scheduleId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task BroadcastSettingsUpdatedAsync(string section, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Minimal <see cref="IMeterFactory"/> so the (sealed) CalluMetrics can be constructed in
/// tests without a DI container. The metrics surface isn't exercised by the paths under test.
/// </summary>
internal sealed class FakeMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);
    public void Dispose() { }
}

/// <summary>
/// Runs the operation inline with no real transaction — lets us unit-test services that wrap
/// work in <see cref="ITransactionManager"/> without a database.
/// </summary>
internal sealed class ImmediateTransactionManager : ITransactionManager
{
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        => operation();

    public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        => operation();

    public bool IsInTransaction() => false;
}

/// <summary>Mirrors the real manager's run-work then SaveChanges then commit, for the in-memory store.</summary>
internal sealed class SavingTransactionManager(ApplicationDbContext context) : ITransactionManager
{
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await operation();
        await context.SaveChangesAsync(cancellationToken);
    }

    public bool IsInTransaction() => false;
}
