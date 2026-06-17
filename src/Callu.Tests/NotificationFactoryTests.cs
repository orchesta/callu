using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;

namespace Callu.Tests;

/// <summary>
/// The dedupe key is the foundation of notification idempotency (and of the H5 batch-safety
/// fix). These lock in its two contracts: identical logical pages collapse to one key, and
/// genuinely distinct pages never collide.
/// </summary>
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
}
