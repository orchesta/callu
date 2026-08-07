using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives the e-mail channel, the one that is on by default and the one where Skipped is honest.</summary>
public class EmailChannelDispatcherTests
{
    private const string UserId = "responder-1";
    private const string Email = "responder@example.io";

    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public EmailChannelDispatcherTests()
    {
        _email.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _email.SendOnCallNotificationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Responder", "+905551112233", Email));
    }

    // ── first dispatch ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmailTheRelayAccepts_IsDelivered()
    {
        var notification = Page();

        await Sut().SendAsync(notification, Email, null, Payload(), "https://callu.example.io/incidents/1");

        await _email.Received(1).SendOnCallNotificationAsync(
            Email, "Checkout failing", "Critical", "https://callu.example.io/incidents/1", Arg.Any<CancellationToken>());

        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
        Assert.True(notification.PageIsOnItsWay);
    }

    /// <summary>
    /// The relay was asked and said no (a 4xx, a greylist, a bounce). That is an attempt: it costs one
    /// of the row's three and the retry sweep owns it from here, so the page is still coming.
    /// </summary>
    [Fact]
    public async Task AnEmailTheRelayRefuses_IsRetriable_AndSpendsAnAttempt()
    {
        _email.SendOnCallNotificationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var notification = Page();

        await Sut().SendAsync(notification, Email, null, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.Equal(1, notification.RetryCount);
        Assert.NotNull(notification.NextRetryAt);
        Assert.True(notification.PageIsOnItsWay);
    }

    /// <summary>An SMTP exception mid-send is the same fact, and must not escape the retry window either.</summary>
    [Fact]
    public async Task AnSmtpFailure_IsRetriable_NotLost()
    {
        _email.SendOnCallNotificationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new System.Net.Mail.SmtpException("relay refused the connection"));

        var notification = Page();

        await Sut().SendAsync(notification, Email, null, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
    }

    /// <summary>
    /// A user with no email address cannot be mailed by any number of retries — that is a data problem,
    /// and it stops. (Note it does NOT count as a page: the step must not report this user as reached.)
    /// </summary>
    [Fact]
    public async Task AUserWithNoEmailAddress_StopsPermanently_AndIsNotCountedAsPaged()
    {
        var notification = Page();

        await Sut().SendAsync(notification, email: null, null, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.PageIsOnItsWay);
    }

    // ── the honest Skipped ────────────────────────────────────────────────────────────────────────

    /// <summary>With no SMTP configured the row is Skipped, and nothing is coming for it.</summary>
    [Fact]
    public async Task WithNoSmtpConfigured_TheRowIsSkipped_AndNothingIsComing()
    {
        _email.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);
        var notification = Page();

        await Sut().SendAsync(notification, Email, null, Payload(), null);

        await _email.DidNotReceive().SendOnCallNotificationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.IsSent);
        Assert.False(notification.PageIsOnItsWay);
    }

    // ── retry ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARetryTheRelayAccepts_IsDelivered()
    {
        var notification = Page();

        await Sut().RetryAsync(notification, "https://callu.example.io");

        await _email.Received(1).SendOnCallNotificationAsync(
            Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
    }

    [Fact]
    public async Task ARetryTheRelayRefuses_StaysRetriable()
    {
        _email.SendOnCallNotificationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var notification = Page();

        await Sut().RetryAsync(notification, "https://callu.example.io");

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
    }

    /// <summary>The user was deleted while the page sat in its backoff — there is nobody left to mail.</summary>
    [Fact]
    public async Task ARetryForAUserWhoIsGone_StopsPermanently()
    {
        _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((UserContactSnapshot?)null);

        var notification = Page();

        await Sut().RetryAsync(notification, "https://callu.example.io");

        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    /// <summary>A refusal on the last attempt ends the chain rather than looping.</summary>
    [Fact]
    public async Task ARetryOnItsLastAttempt_ThatIsRefused_DiesRatherThanLoopingForever()
    {
        _email.SendOnCallNotificationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var notification = Page();
        notification.RetryCount = Notification.MaxRetries - 1;

        await Sut().RetryAsync(notification, "https://callu.example.io");

        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    // ── the "send test notification" button ───────────────────────────────────────────────────────

    [Fact]
    public async Task SendTest_WithNoSmtp_TellsTheOperatorTheTruth()
    {
        _email.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (success, message) = await Sut().SendTestAsync(UserId, Email, null);

        Assert.False(success);
        Assert.Contains("SMTP", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_WithNoAddressOnTheProfile_SaysSo()
    {
        var (success, message) = await Sut().SendTestAsync(UserId, email: null, null);

        Assert.False(success);
        Assert.Contains("email", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_OnAWorkingRelay_ActuallySendsTheMail()
    {
        var (success, _) = await Sut().SendTestAsync(UserId, Email, null);

        Assert.True(success);
        await _email.Received(1).SendOnCallNotificationAsync(
            Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private EmailChannelDispatcher Sut() =>
        new EmailChannelDispatcher(
            _email,
            _contacts,
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<EmailChannelDispatcher>.Instance);

    private static NotificationPayload Payload() => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private static Notification Page() => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationType.Email,
        Title = "Escalation step 1",
        Message = "Checkout failing",
        IncidentId = Guid.NewGuid(),
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };
}
