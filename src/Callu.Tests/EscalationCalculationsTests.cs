using Callu.Infrastructure.Utilities;

namespace Callu.Tests;

public class EscalationCalculationsTests
{
    private static readonly DateTime Anchor = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldTriggerStep_True_WhenDelayElapsed()
    {
        Assert.True(EscalationCalculations.ShouldTriggerStep(Anchor, 5, Anchor.AddMinutes(6)));
    }

    [Fact]
    public void ShouldTriggerStep_True_AtExactBoundary()
    {
        Assert.True(EscalationCalculations.ShouldTriggerStep(Anchor, 5, Anchor.AddMinutes(5)));
    }

    [Fact]
    public void ShouldTriggerStep_False_BeforeDelay()
    {
        Assert.False(EscalationCalculations.ShouldTriggerStep(Anchor, 5, Anchor.AddMinutes(4).AddSeconds(59)));
    }

    [Fact]
    public void ShouldTriggerStep_ZeroDelay_FiresImmediately()
    {
        Assert.True(EscalationCalculations.ShouldTriggerStep(Anchor, 0, Anchor));
    }
}
