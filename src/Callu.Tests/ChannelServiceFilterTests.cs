using Callu.Infrastructure.Services;

namespace Callu.Tests;

public class ChannelServiceFilterTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();

    [Fact]
    public void NoFilter_ReceivesEverything_IncludingServicelessIncidents()
    {
        Assert.True(NotificationChannelService.ChannelServesIncident([], ServiceA));
        Assert.True(NotificationChannelService.ChannelServesIncident([], null));
    }

    [Fact]
    public void Filtered_ReceivesAServiceOnItsAllowlist()
    {
        Assert.True(NotificationChannelService.ChannelServesIncident([ServiceA, ServiceB], ServiceA));
    }

    [Fact]
    public void Filtered_SkipsAServiceNotOnItsAllowlist()
    {
        Assert.False(NotificationChannelService.ChannelServesIncident([ServiceB], ServiceA));
    }

    [Fact]
    public void Filtered_SkipsAServicelessIncident_RatherThanLeakingToIt()
    {
        Assert.False(NotificationChannelService.ChannelServesIncident([ServiceA], null));
    }
}
