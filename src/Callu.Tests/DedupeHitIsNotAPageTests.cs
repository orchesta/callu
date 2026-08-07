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

/// <summary>A dedupe hit says a row exists; whether anybody was paged is decided by that row's own status.</summary>
public class DedupeHitIsNotAPageTests : IDisposable
{
    private const string Responder = "responder-1";
    private static readonly Guid IncidentId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"dedupe-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public DedupeHitIsNotAPageTests() =>
        _contacts.GetContactByIdAsync(Responder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(Responder, "Responder", "+905551112233", "responder@example.io"));

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── the lie: a dead row counted as a reached responder ────────────────────────────────────────

    /// <summary>A redispatch that dedupes onto a Skipped row has paged nobody, and says so.</summary>
    [Fact]
    public async Task ARedispatchOntoASkippedRow_ReachesNobody()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(NotificationDeliveryStatus.Skipped, "no sms provider is registered");

        var channel = new RecordingChannelDispatcher(NotificationType.Sms);
        var result = await Dispatcher(channel).NotifyUsersAsync([Responder], Payload());

        Assert.Equal(0, result.Reached);

        // ...and the operator is told WHY, with the dead row's own reason, rather than being left with a
        // step that quietly reached nobody.
        Assert.True(result.ChannelsSilent);
        var silence = Assert.Single(result.ChannelSilences!);
        Assert.Equal(NotificationType.Sms, silence.Channel);
        Assert.True(silence.IsPermanent, "a Skipped row is not coming back; the escalation must not wait on it");
        Assert.Contains("provider", silence.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The other dead status, reached the other way: three providers said no, out loud.</summary>
    [Fact]
    public async Task ARedispatchOntoAPermanentlyFailedRow_ReachesNobody()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(NotificationDeliveryStatus.PermanentlyFailed, "number unreachable");

        var result = await Dispatcher(new RecordingChannelDispatcher(NotificationType.Sms))
            .NotifyUsersAsync([Responder], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
        Assert.True(Assert.Single(result.ChannelSilences!).IsPermanent);
    }

    /// <summary>A dead earlier row is still a dedupe hit: no second message is sent, only the reporting changes.</summary>
    [Fact]
    public async Task ARedispatchOntoADeadRow_StillDoesNotSendASecondPage()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(NotificationDeliveryStatus.Skipped, "no sms provider is registered");

        var channel = new RecordingChannelDispatcher(NotificationType.Sms);
        await Dispatcher(channel).NotifyUsersAsync([Responder], Payload());

        Assert.Empty(channel.Sent);
        Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());
    }

    // ── the thing the fix must not break: a LIVE row still suppresses a second page ───────────────

    /// <summary>A redispatch onto a live row counts as reached and queues no second page.</summary>
    [Theory]
    [InlineData(NotificationDeliveryStatus.Sending)]      // a concurrent dispatch has it right now
    [InlineData(NotificationDeliveryStatus.Retrying)]
    [InlineData(NotificationDeliveryStatus.Delivered)]    // it went out
    [InlineData(NotificationDeliveryStatus.Failed)]       // refused, budget left, the sweep owns it
    public async Task ARedispatchOntoALiveRow_StillCountsAsReached_AndPagesNobodyTwice(
        NotificationDeliveryStatus alive)
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(alive, "in flight");

        var channel = new RecordingChannelDispatcher(NotificationType.Sms);
        var result = await Dispatcher(channel).NotifyUsersAsync([Responder], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent);

        Assert.Empty(channel.Sent);
        Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());
    }

    /// <summary>A redispatch onto a deferred row counts as reached and is reported as queued rather than sent.</summary>
    [Fact]
    public async Task ARedispatchOntoADeferredRow_CountsAsReached_AndIsReportedAsQueuedNotSent()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(
            NotificationDeliveryStatus.Pending,
            "no sms provider is registered",
            nextRetryAt: DateTime.UtcNow.AddSeconds(60));

        var result = await Dispatcher(new RecordingChannelDispatcher(NotificationType.Sms))
            .NotifyUsersAsync([Responder], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent);

        // Reached, but NOT yet rung — and the operator is told that too, from the existing row.
        var deferral = Assert.Single(result.Deferrals!);
        Assert.Equal(NotificationType.Sms, deferral.Channel);
    }

    /// <summary>A Pending row with no budget left is outside the claim window, so it reaches nobody.</summary>
    [Fact]
    public async Task ARedispatchOntoAPendingRowWithNoBudgetLeft_ReachesNobody()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(
            NotificationDeliveryStatus.Pending,
            "no sms provider is registered",
            nextRetryAt: DateTime.UtcNow.AddSeconds(60),
            retryCount: Notification.MaxRetries);

        var result = await Dispatcher(new RecordingChannelDispatcher(NotificationType.Sms))
            .NotifyUsersAsync([Responder], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
    }

    // ── the row that is dead without saying so: soft-deleted ──────────────────────────────────────

    /// <summary>A soft-deleted row is one the sweep will never touch, so no status on it counts as reached.</summary>
    [Theory]
    [InlineData(NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.Retrying)]
    [InlineData(NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Delivered)]
    public async Task ARedispatchOntoASoftDeletedRow_ReachesNobody_AndSaysSo(NotificationDeliveryStatus status)
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(status, "queued", isDeleted: true);

        var channel = new RecordingChannelDispatcher(NotificationType.Sms);
        var result = await Dispatcher(channel).NotifyUsersAsync([Responder], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent,
            "the row is soft-deleted: the retry sweep will not claim it and the reaper will not reclaim it, "
            + "so nothing is coming and the escalation must be told");

        var silence = Assert.Single(result.ChannelSilences!);
        Assert.True(silence.IsPermanent,
            "a deleted row is not coming back — an escalation that waits on it waits forever");
    }

    /// <summary>The deferred flavour reads most convincingly as alive, and still reaches nobody.</summary>
    [Fact]
    public async Task ARedispatchOntoASoftDeletedDeferredRow_ReachesNobody()
    {
        await SeedResponderAsync();
        await SeedExistingPageAsync(
            NotificationDeliveryStatus.Pending,
            "no sms provider is registered",
            nextRetryAt: DateTime.UtcNow.AddSeconds(60),
            isDeleted: true);

        var result = await Dispatcher(new RecordingChannelDispatcher(NotificationType.Sms))
            .NotifyUsersAsync([Responder], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
        Assert.Null(result.Deferrals);   // nothing is queued: claiming a deferral here is the lie itself
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The step being dispatched. The DedupeKey the funnel computes for it is deterministic.</summary>
    private static NotificationPayload Payload() => new()
    {
        IncidentId = IncidentId,
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    /// <summary>The row an earlier dispatch of the same step left behind, keyed the way the funnel keys it.</summary>
    private async Task SeedExistingPageAsync(
        NotificationDeliveryStatus status,
        string reason,
        DateTime? nextRetryAt = null,
        int retryCount = 0,
        bool isDeleted = false)
    {
        _ctx.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Responder,
            IncidentId = IncidentId,
            Type = NotificationType.Sms,
            Title = "[Critical] Checkout failing",
            Message = "EscalationStep: Escalation Level 1",
            DeliveryStatus = status,
            ErrorMessage = reason,
            NextRetryAt = nextRetryAt,
            RetryCount = retryCount,
            IsDeleted = isDeleted,
            IsSent = status == NotificationDeliveryStatus.Delivered,
            DedupeKey = NotificationFactory.ComputeDedupeKey(
                Responder, Payload(), NotificationType.Sms, Payload().DispatchGeneration),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }

    /// <summary>SMS-only, push off: one paging channel, so "reached" is about that channel and nothing else.</summary>
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
