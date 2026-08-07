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

    // The first step is not spaced from anything, so a policy that says "page immediately" must.
    // Flooring it here would silently hold every first page for two minutes.
    [Fact]
    public void FirstStepDueAt_TakesTheDelayAsWritten()
    {
        Assert.Equal(Anchor, EscalationCalculations.FirstStepDueAt(Anchor, 0));
        Assert.Equal(Anchor.AddMinutes(5), EscalationCalculations.FirstStepDueAt(Anchor, 5));
    }

    [Fact]
    public void NextStepDueAt_FloorsAZeroDelayAtTheMinimumGap()
    {
        Assert.Equal(
            Anchor.AddMinutes(EscalationCalculations.MinDelayMinutesBetweenSteps),
            EscalationCalculations.NextStepDueAt(Anchor, 0));
    }

    [Fact]
    public void NextStepDueAt_KeepsADelayLongerThanTheFloor()
    {
        Assert.Equal(Anchor.AddMinutes(9), EscalationCalculations.NextStepDueAt(Anchor, 9));
    }

    // A step that reached nobody backdates the clock so the next one goes on the following tick.
    // Reporting that as a future instant would show a countdown for a page already on its way.
    [Fact]
    public void NextStepDueAt_IsInThePast_WhenTheClockWasBackdated()
    {
        var backdated = Anchor.AddMinutes(-(EscalationCalculations.MinDelayMinutesBetweenSteps + 1));

        Assert.True(EscalationCalculations.NextStepDueAt(backdated, 0) < Anchor);
    }

    [Fact]
    public void DueAt_AgreesWithShouldTriggerStep_OnTheSameInputs()
    {
        var due = EscalationCalculations.NextStepDueAt(Anchor, 7);

        Assert.False(EscalationCalculations.ShouldTriggerStep(Anchor, 7, due.AddSeconds(-1)));
        Assert.True(EscalationCalculations.ShouldTriggerStep(Anchor, 7, due));
    }
}
