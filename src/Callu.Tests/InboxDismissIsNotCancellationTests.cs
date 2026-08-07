using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Hiding a page from the notification bell leaves the dispatch row exactly as live as it was.</summary>
public class InboxDismissIsNotCancellationTests : IDisposable
{
    private const string Responder = "responder-1";
    private static readonly Guid IncidentId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"inbox-dismiss-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public InboxDismissIsNotCancellationTests() =>
        _contacts.GetContactByIdAsync(Responder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(Responder, "Responder", "+905551112233", "responder@example.io"));

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── entity: dismissing is invisible to dispatch ───────────────────────────────────────────────

    /// <summary>Dismissing a Delivered row leaves CountsAsReached true, because the dismiss flag does not participate.</summary>
    [Fact]
    public void DismissingADeliveredRow_LeavesCountsAsReachedTrue()
    {
        var live = DeliveredSmsRow();
        var dismissed = DeliveredSmsRow();
        dismissed.InboxDismissedAt = DateTime.UtcNow;

        Assert.True(live.CountsAsReached);
        Assert.True(dismissed.CountsAsReached);
        Assert.False(dismissed.IsDeleted);
        Assert.True(dismissed.RetrySweepWillTakeIt == live.RetrySweepWillTakeIt);
    }

    /// <summary>The other direction, for contrast: a genuine soft-delete DOES kill the row.</summary>
    [Fact]
    public void SoftDeletingADeliveredRow_KillsCountsAsReached()
    {
        var row = DeliveredSmsRow();
        row.IsDeleted = true;

        Assert.False(row.CountsAsReached);
        Assert.False(row.RetrySweepWillTakeIt);
    }

    // ── the headline: a dedupe hit onto a hidden-but-live row still reaches, and rings nobody twice ──

    /// <summary>A redispatch onto a dismissed Delivered row still reaches, and does not page the responder twice.</summary>
    [Fact]
    public async Task ARedispatchOntoADismissedDeliveredRow_StillReaches_AndDoesNotPageTwice()
    {
        await SeedResponderAsync();

        var existing = DeliveredSmsRow();
        existing.InboxDismissedAt = DateTime.UtcNow;   // hidden from the inbox, but NOT cancelled
        _ctx.Notifications.Add(existing);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var channel = new RecordingChannelDispatcher(NotificationType.Sms);
        var result = await Dispatcher(channel).NotifyUsersAsync([Responder], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent);

        Assert.Empty(channel.Sent);                                        // rang nobody twice
        Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());

        // and the row it hit is still hidden and still NOT soft-deleted
        var stored = await _ctx.Notifications.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.False(stored.IsDeleted);
        Assert.NotNull(stored.InboxDismissedAt);
    }

    // ── query service: DELETE hides, does not soft-delete ─────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SetsInboxDismissedAt_AndLeavesIsDeletedUntouched()
    {
        var n = EmailRow(isRead: true);
        _ctx.Notifications.Add(n);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var ok = await QueryService().DeleteAsync(n.Id, Responder);
        Assert.True(ok);

        var stored = await _ctx.Notifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == n.Id);
        Assert.NotNull(stored.InboxDismissedAt);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_IsScopedToOwner_AndIsIdempotentlyNotFoundOnceHidden()
    {
        var n = EmailRow(isRead: true);
        _ctx.Notifications.Add(n);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        Assert.False(await QueryService().DeleteAsync(n.Id, "someone-else"));   // not the owner
        Assert.True(await QueryService().DeleteAsync(n.Id, Responder));         // hidden now
        Assert.False(await QueryService().DeleteAsync(n.Id, Responder));        // already hidden → 404
    }

    [Fact]
    public async Task GetRecentAsync_DoesNotShowADismissedNotification()
    {
        var visible = EmailRow(isRead: false);
        var hidden = EmailRow(isRead: false);
        hidden.InboxDismissedAt = DateTime.UtcNow;
        _ctx.Notifications.AddRange(visible, hidden);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var recent = (await QueryService().GetRecentAsync(Responder, count: 50)).ToList();

        Assert.Single(recent);
        Assert.Equal(visible.Id, recent[0].Id);
    }

    [Fact]
    public async Task GetUnreadCount_DoesNotCountADismissedNotification()
    {
        var counted = EmailRow(isRead: false);
        var hidden = EmailRow(isRead: false);
        hidden.InboxDismissedAt = DateTime.UtcNow;
        _ctx.Notifications.AddRange(counted, hidden);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        Assert.Equal(1, await QueryService().GetUnreadCountAsync(Responder));
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private static NotificationPayload Payload() => new()
    {
        IncidentId = IncidentId,
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    /// <summary>A Delivered SMS page under the dedupe key the funnel is about to compute for this step.</summary>
    private static Notification DeliveredSmsRow() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Responder,
        IncidentId = IncidentId,
        Type = NotificationType.Sms,
        Title = "[Critical] Checkout failing",
        Message = "EscalationStep: Escalation Level 1",
        DeliveryStatus = NotificationDeliveryStatus.Delivered,
        IsSent = true,
        DedupeKey = NotificationFactory.ComputeDedupeKey(
            Responder, Payload(), NotificationType.Sms, Payload().DispatchGeneration),
        CreatedAt = DateTime.UtcNow.AddMinutes(-2)
    };

    private static Notification EmailRow(bool isRead) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Responder,
        IncidentId = IncidentId,
        Type = NotificationType.Email,
        Title = "Database unreachable",
        Message = "Escalation",
        IsRead = isRead,
        DeliveryStatus = NotificationDeliveryStatus.Delivered,
        CreatedAt = DateTime.UtcNow.AddMinutes(-1)
    };

    private async Task SeedResponderAsync()
    {
        _ctx.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = Responder,
            EmailEnabled = false,
            SmsEnabled = true,
            VoiceEnabled = false,
            PushEnabled = false,
            Timezone = "UTC"
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }

    private NotificationQueryService QueryService() =>
        new(new NotificationRepository(_ctx, NullLogger<NotificationRepository>.Instance),
            new SavingTransactionManager(_ctx));

    private NotificationDispatcher Dispatcher(INotificationChannelDispatcher channel)
    {
        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        return new NotificationDispatcher(
            new NotificationRepository(_ctx, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(_ctx, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            _contacts,
            Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Instant.FromUtc(2026, 7, 14, 12, 0)),
            [channel],
            _ctx,
            NullLogger<NotificationDispatcher>.Instance);
    }
}
