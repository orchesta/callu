using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using Dispatcher = Callu.Infrastructure.Services.NotificationDispatcher;

namespace Callu.Tests;

/// <summary>
/// Providers are asked one row at a time. A self-hosted voice service renders every prompt before it
/// answers, so one wedged container used to hold a whole pass — and behind it every e-mail and SMS
/// page for every other incident. A channel now gets a bounded share of each pass; what it does not
/// finish is written down as queued and picked up by the next sweep.
/// </summary>
public class NotificationDispatchBudgetTests : IDisposable
{
    private static readonly Instant Start = Instant.FromUtc(2026, 7, 30, 3, 0);

    private readonly ApplicationDbContext _ctx =
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dispatch-budget-{Guid.NewGuid():N}").Options);

    private readonly AdvancingClock _clock = new(Start);
    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public void Dispose() => _ctx.Dispose();

    /// <summary>A clock the fake provider moves, so a slow render costs time without a test sleeping.</summary>
    private sealed class AdvancingClock(Instant now) : IClock
    {
        private Instant _now = now;

        public Instant GetCurrentInstant() => _now;

        public void Advance(TimeSpan by) => _now += Duration.FromTimeSpan(by);
    }

    /// <summary>A channel whose provider takes <paramref name="perCall"/> of wall clock to answer.</summary>
    private sealed class SlowChannelDispatcher(
        NotificationType channel, AdvancingClock clock, TimeSpan perCall) : INotificationChannelDispatcher
    {
        public NotificationType Channel => channel;

        public List<Guid> Sent { get; } = [];

        public Task SendAsync(
            Notification notification, string? email, string? phoneNumber,
            NotificationPayload payload, string? incidentUrl, CancellationToken cancellationToken = default)
        {
            Sent.Add(notification.Id);
            clock.Advance(perCall);
            notification.MarkDelivered();
            return Task.CompletedTask;
        }

        public Task RetryAsync(Notification notification, string? baseUrl, CancellationToken cancellationToken = default)
        {
            Sent.Add(notification.Id);
            clock.Advance(perCall);
            notification.MarkDelivered();
            return Task.CompletedTask;
        }

        public Task<(bool Success, string Message)> SendTestAsync(
            string userId, string? email, string? phoneNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, "ok"));
    }

    private Dispatcher Sut(IEnumerable<INotificationChannelDispatcher> channels)
    {
        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        return new Dispatcher(
            new NotificationRepository(_ctx, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(_ctx, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            _contacts,
            Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            _clock,
            channels,
            _ctx,
            NullLogger<Dispatcher>.Instance);
    }

    private List<string> SeedResponders(int count)
    {
        var userIds = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var userId = $"responder-{i}";
            userIds.Add(userId);

            _contacts.GetContactByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(new UserContactSnapshot(userId, $"Responder {i}", "+90532123456" + i, $"{userId}@example.io"));

            _ctx.NotificationPreferences.Add(new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EmailEnabled = true,
                VoiceEnabled = true,
                SmsEnabled = false,
                PushEnabled = false,
                Timezone = "UTC"
            });
        }

        _ctx.SaveChanges();
        _ctx.ChangeTracker.Clear();

        return userIds;
    }

    private static NotificationPayload Payload() => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Database unreachable",
        Severity = "High",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private List<Notification> Rows(NotificationType channel) =>
        [.. _ctx.Notifications.AsNoTracking().Where(n => n.Type == channel)];

    // ------------------------------------------------------------------ the bound

    /// <summary>
    /// Forty-five seconds a call, sixty seconds of budget: two calls are placed and the rest of the
    /// pass costs the wedged provider nothing more. Without the bound the pass runs as long as the
    /// rota is deep.
    /// </summary>
    [Fact]
    public async Task AWedgedVoiceProviderGetsABoundedShareOfOnePass()
    {
        var userIds = SeedResponders(6);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, _clock, TimeSpan.FromSeconds(45));
        var email = new SlowChannelDispatcher(NotificationType.Email, _clock, TimeSpan.Zero);

        await Sut([email, voice]).NotifyUsersAsync(userIds, Payload());

        Assert.Equal(2, voice.Sent.Count);
        Assert.True(_clock.GetCurrentInstant() - Start <= Duration.FromSeconds(105),
            "one pass spent more wall clock than one channel's budget plus one attempt");
    }

    /// <summary>The point of bounding it: the channels that were not slow still went out on this pass.</summary>
    [Fact]
    public async Task EveryOtherChannelStillGoesOutOnTheSamePass()
    {
        var userIds = SeedResponders(6);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, _clock, TimeSpan.FromSeconds(45));
        var email = new SlowChannelDispatcher(NotificationType.Email, _clock, TimeSpan.Zero);

        await Sut([email, voice]).NotifyUsersAsync(userIds, Payload());

        Assert.Equal(6, email.Sent.Count);
        Assert.All(Rows(NotificationType.Email), r => Assert.Equal(NotificationDeliveryStatus.Delivered, r.DeliveryStatus));
    }

    /// <summary>
    /// A page held back is queued, not lost: the row is written, keeps its retry budget, carries a
    /// deadline, and still counts as a page on its way — the sweep owns it.
    /// </summary>
    [Fact]
    public async Task ThePagesThatDidNotFitAreQueued_NotDropped()
    {
        var userIds = SeedResponders(6);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, _clock, TimeSpan.FromSeconds(45));

        await Sut([voice]).NotifyUsersAsync(userIds, Payload());

        var queued = Rows(NotificationType.VoiceCall)
            .Where(r => r.DeliveryStatus != NotificationDeliveryStatus.Delivered)
            .ToList();

        Assert.Equal(4, queued.Count);
        Assert.All(queued, row =>
        {
            Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
            Assert.NotNull(row.NextRetryAt);
            Assert.Equal(0, row.RetryCount);
            Assert.True(row.RetrySweepWillTakeIt);
            Assert.True(row.PageIsOnItsWay);
        });
    }

    /// <summary>
    /// Held back is not silence. A responder whose page is queued was reached for the step's purposes,
    /// or escalation walks past a policy whose calls are still coming.
    /// </summary>
    [Fact]
    public async Task AHeldBackPageIsReportedAsQueued_NotAsAChannelThatSentNothing()
    {
        var userIds = SeedResponders(6);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, _clock, TimeSpan.FromSeconds(45));

        var result = await Sut([voice]).NotifyUsersAsync(userIds, Payload());

        Assert.Equal(6, result.Reached);
        Assert.Equal(0, result.Silent);
        Assert.True(result.HasDeferredPages);
        Assert.Equal(4, result.DeferredChannelCount);
    }

    /// <summary>A provider that answers promptly is never held back, however long the batch is.</summary>
    [Fact]
    public async Task APromptProviderDrainsTheWholeBatch()
    {
        var userIds = SeedResponders(20);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, _clock, TimeSpan.FromSeconds(2));

        await Sut([voice]).NotifyUsersAsync(userIds, Payload());

        Assert.Equal(20, voice.Sent.Count);
    }

    /// <summary>
    /// A single cold render is still allowed to take its time — cutting it short would turn a slow
    /// page into no page, which is the reason the shared 15s attempt cap is not applied to this one.
    /// </summary>
    [Fact]
    public async Task OneRenderLongerThanTheWholeBudgetStillCompletes()
    {
        var userIds = SeedResponders(3);
        var voice = new SlowChannelDispatcher(
            NotificationType.VoiceCall, _clock, Dispatcher.PerChannelDispatchBudget + TimeSpan.FromSeconds(30));

        await Sut([voice]).NotifyUsersAsync(userIds, Payload());

        Assert.Single(voice.Sent);
        Assert.Equal(
            NotificationDeliveryStatus.Delivered,
            Rows(NotificationType.VoiceCall).Single(r => r.Id == voice.Sent[0]).DeliveryStatus);
    }
}
