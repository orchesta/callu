using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;

namespace Callu.Tests;

/// <summary>Locks in the dedupe key's two contracts: identical pages collapse, distinct pages never collide.</summary>
public class NotificationFactoryTests
{
    private static NotificationPayload Payload(Guid incidentId, int level = 1) => new()
    {
        IncidentId = incidentId,
        Title = "Database unreachable",
        Severity = "High",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = level
    };

    [Fact]
    public void ComputeDedupeKey_IsDeterministic_AndNonEmpty()
    {
        var incident = Guid.NewGuid();
        var k1 = NotificationFactory.ComputeDedupeKey("user-1", Payload(incident), NotificationType.Email);
        var k2 = NotificationFactory.ComputeDedupeKey("user-1", Payload(incident), NotificationType.Email);

        Assert.Equal(k1, k2);
        Assert.False(string.IsNullOrEmpty(k1));
    }

    [Fact]
    public void ComputeDedupeKey_IgnoresUserIdCasing()
    {
        var incident = Guid.NewGuid();
        var upper = NotificationFactory.ComputeDedupeKey("User-ABC", Payload(incident), NotificationType.Email);
        var lower = NotificationFactory.ComputeDedupeKey("user-abc", Payload(incident), NotificationType.Email);

        Assert.Equal(upper, lower);
    }

    [Fact]
    public void ComputeDedupeKey_Differs_ByChannel()
    {
        var incident = Guid.NewGuid();
        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.Email),
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.Push));
    }

    [Fact]
    public void ComputeDedupeKey_Differs_ByEscalationLevel()
    {
        var incident = Guid.NewGuid();
        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("u", Payload(incident, 1), NotificationType.Email),
            NotificationFactory.ComputeDedupeKey("u", Payload(incident, 2), NotificationType.Email));
    }

    [Fact]
    public void ComputeDedupeKey_Differs_ByRetryGeneration()
    {
        var incident = Guid.NewGuid();
        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.Email, 0),
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.Email, 1));
    }

    [Fact]
    public void ComputeDedupeKey_Differs_ByUser()
    {
        var incident = Guid.NewGuid();
        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("user-a", Payload(incident), NotificationType.Email),
            NotificationFactory.ComputeDedupeKey("user-b", Payload(incident), NotificationType.Email));
    }

    [Fact]
    public void ComputeDedupeKey_Differs_ByIncident()
    {
        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("u", Payload(Guid.NewGuid()), NotificationType.Email),
            NotificationFactory.ComputeDedupeKey("u", Payload(Guid.NewGuid()), NotificationType.Email));
    }

    /// <summary>Only the dispatch generation tells a reopened run's page apart from the first run's.</summary>
    [Fact]
    public void Create_ReopenedRun_ProducesADifferentKey_ForAnOtherwiseIdenticalPage()
    {
        var incident = Guid.NewGuid();
        var firstRun = Payload(incident) with { DispatchGeneration = 638_000_000_000_000_000 };
        var afterReopen = Payload(incident) with { DispatchGeneration = 638_000_000_000_000_001 };

        var a = NotificationFactory.Create("u", firstRun, "/incidents/x", NotificationType.VoiceCall, firstRun.DispatchGeneration);
        var b = NotificationFactory.Create("u", afterReopen, "/incidents/x", NotificationType.VoiceCall, afterReopen.DispatchGeneration);

        Assert.NotEqual(a.DedupeKey, b.DedupeKey);
    }

    /// <summary>The key is sensitive to a single tick, which is why the generation is truncated before it arrives.</summary>
    [Fact]
    public void ComputeDedupeKey_IsSensitive_ToSubMicrosecondGenerationDrift()
    {
        var incident = Guid.NewGuid();
        var ticks = new DateTime(2026, 7, 13, 9, 30, 15, DateTimeKind.Utc).Ticks + 7;

        Assert.NotEqual(
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.VoiceCall, ticks),
            NotificationFactory.ComputeDedupeKey("u", Payload(incident), NotificationType.VoiceCall, ticks - 7));
    }

    /// <summary>
    /// The flip side: a crash-replay of the SAME run must still collapse to one page.
    /// </summary>
    [Fact]
    public void Create_SameGeneration_IsStillIdempotent()
    {
        var payload = Payload(Guid.NewGuid()) with { DispatchGeneration = 42 };

        var a = NotificationFactory.Create("u", payload, "/incidents/x", NotificationType.Email, payload.DispatchGeneration);
        var b = NotificationFactory.Create("u", payload, "/incidents/x", NotificationType.Email, payload.DispatchGeneration);

        Assert.Equal(a.DedupeKey, b.DedupeKey);
    }
}
