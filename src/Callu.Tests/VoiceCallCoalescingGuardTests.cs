using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>
/// The per-user voice-call cooldown defers a call for a DIFFERENT incident, never throttles
/// same-incident re-dials, and stays inert when the feature is off.
/// </summary>
public class VoiceCallCoalescingGuardTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"b0-{Guid.NewGuid():N}")
            .Options);

    private static VoiceCallCoalescingGuard Guard(ApplicationDbContext ctx, int cooldownSeconds) =>
        new(new NotificationRepository(ctx, NullLogger<NotificationRepository>.Instance),
            Options.Create(new VoiceCallCoalescingOptions { VoiceCallCooldownSeconds = cooldownSeconds }));

    private static Notification VoiceNotif(
        string userId, Guid incidentId, NotificationDeliveryStatus status, DateTime anchor) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        UserId = userId,
        Type = NotificationType.VoiceCall,
        Title = "t",
        Message = "m",
        DeliveryStatus = status,
        SentAt = status == NotificationDeliveryStatus.Delivered ? anchor : null,
        LastAttemptAt = anchor,
        CreatedAt = anchor,
        UpdatedAt = anchor,
    };

    [Fact]
    public async Task Disabled_ByDefault_ReturnsNull()
    {
        using var ctx = NewContext();
        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Delivered, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 0).GetDeferralUntilAsync("u1", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task RecentDeliveredCall_FromOtherIncident_Defers()
    {
        using var ctx = NewContext();
        var sentAt = DateTime.UtcNow.AddSeconds(-10);
        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Delivered, sentAt));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 120).GetDeferralUntilAsync("u1", Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(sentAt.AddSeconds(120), result!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task InFlightCall_FromOtherIncident_Defers()
    {
        using var ctx = NewContext();
        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Sending, DateTime.UtcNow.AddSeconds(-5)));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 120).GetDeferralUntilAsync("u1", Guid.NewGuid());

        Assert.NotNull(result);
    }

    /// <summary>The retry path excludes in-flight rows and anchors only on delivered calls.</summary>
    [Fact]
    public async Task RetryPath_ExcludesInFlightRows_AnchorsOnDeliveredOnly()
    {
        using var ctx = NewContext();
        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Sending, DateTime.UtcNow.AddSeconds(-5)));
        await ctx.SaveChangesAsync();

        var guard = Guard(ctx, cooldownSeconds: 120);

        Assert.Null(await guard.GetDeferralUntilAsync("u1", Guid.NewGuid(), includeInFlight: false));

        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Delivered, DateTime.UtcNow.AddSeconds(-10)));
        await ctx.SaveChangesAsync();

        Assert.NotNull(await guard.GetDeferralUntilAsync("u1", Guid.NewGuid(), includeInFlight: false));
    }

    [Fact]
    public async Task SameIncident_Redial_IsNeverThrottled()
    {
        using var ctx = NewContext();
        var incidentId = Guid.NewGuid();
        ctx.Notifications.Add(VoiceNotif("u1", incidentId, NotificationDeliveryStatus.Delivered, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 120).GetDeferralUntilAsync("u1", incidentId);

        Assert.Null(result);
    }

    [Fact]
    public async Task OldCall_OutsideCooldown_DoesNotDefer()
    {
        using var ctx = NewContext();
        ctx.Notifications.Add(VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Delivered, DateTime.UtcNow.AddMinutes(-10)));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 120).GetDeferralUntilAsync("u1", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task OtherUser_RecentCall_DoesNotDefer()
    {
        using var ctx = NewContext();
        ctx.Notifications.Add(VoiceNotif("u2", Guid.NewGuid(), NotificationDeliveryStatus.Delivered, DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        var result = await Guard(ctx, cooldownSeconds: 120).GetDeferralUntilAsync("u1", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void MarkDeferred_KeepsRetryBudget_AndStaysInRetryWindow()
    {
        var n = VoiceNotif("u1", Guid.NewGuid(), NotificationDeliveryStatus.Pending, DateTime.UtcNow);
        var until = DateTime.UtcNow.AddSeconds(90);

        n.MarkDeferred(until, "voice cooldown");

        Assert.Equal(NotificationDeliveryStatus.Pending, n.DeliveryStatus);
        Assert.Equal(until, n.NextRetryAt);
        Assert.Equal(0, n.RetryCount);
    }
}
