using System.Text.RegularExpressions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>Holds down one sentence: Skipped means nothing was sent and nothing ever will be.</summary>
public class SkippedMeansNothingIsComingTests
{
    /// <summary>The statuses that mean a page is still coming — the retry sweep's window, and nothing else.</summary>
    private static readonly NotificationDeliveryStatus[] StillComing =
    [
        NotificationDeliveryStatus.Pending,   // deferred: no provider yet, or a voice cooldown
        NotificationDeliveryStatus.Failed     // the provider said no, and there is budget left
    ];

    /// <summary>The statuses that mean it is over — either it arrived, or nothing ever will.</summary>
    private static readonly NotificationDeliveryStatus[] Over =
    [
        NotificationDeliveryStatus.Delivered,
        NotificationDeliveryStatus.Skipped,
        NotificationDeliveryStatus.PermanentlyFailed
    ];

    /// <summary>The statuses that mean a send is happening right now.</summary>
    private static readonly NotificationDeliveryStatus[] InFlight =
    [
        NotificationDeliveryStatus.Sending,
        NotificationDeliveryStatus.Retrying
    ];

    // ── 1. the sweep's window is MAGIC NUMBERS in raw SQL ─────────────────────────────────────────

    /// <summary>The sweep's claim query, read out of the source it is hand-written in.</summary>
    private static string ClaimSql() => File.ReadAllText(Path.Combine(
        SourceScanner.Root().FullName, "Callu.Infrastructure", "Services", "NotificationDispatcher.cs"));

    /// <summary>The reaper's source with comments and string literals blanked, so a text scan sees only code.</summary>
    private static string ReaperCode() => SourceScanner.Code(Path.Combine(
        SourceScanner.Root().FullName, "Callu.Infrastructure", "BackgroundJobs",
        "NotificationReaperBackgroundService.cs"));

    /// <summary>The integers in the sweep's hand-written claim SQL are exactly the Pending and Failed statuses.</summary>
    [Fact]
    public void TheRetrySweepsClaimWindow_IsExactlyPendingAndFailed()
    {
        Assert.Equal(0, (int)NotificationDeliveryStatus.Pending);
        Assert.Equal(3, (int)NotificationDeliveryStatus.Failed);

        var claim = Regex.Match(ClaimSql(), @"""DeliveryStatus""\s+IN\s*\(\s*(?<statuses>[\d\s,]+?)\s*\)");

        Assert.True(claim.Success,
            "the retry sweep's claim query no longer selects on \"DeliveryStatus\" IN (...). If the claim moved, "
            + "move this guard with it — the integers in that SQL are the only thing deciding which pages get a "
            + "second chance, and nothing else in the build checks them against the enum.");

        var claimed = claim.Groups["statuses"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .Order()
            .ToArray();

        Assert.Equal(StillComing.Select(s => (int)s).Order().ToArray(), claimed);

        // Said the other way round, because this is the half that loses a page for good:
        Assert.DoesNotContain((int)NotificationDeliveryStatus.Skipped, claimed);
        Assert.DoesNotContain((int)NotificationDeliveryStatus.PermanentlyFailed, claimed);
    }

    /// <summary>The attempt bound in the sweep's SQL must evaluate to Notification.MaxRetries and nothing else.</summary>
    [Fact]
    public void TheRetrySweepsAttemptBound_IsNotionOfMaxRetries_AndNobodyElses()
    {
        var bound = Regex.Match(ClaimSql(), @"""RetryCount""\s*<\s*(?<bound>\{[^}]+\}|\d+)");

        Assert.True(bound.Success,
            "the retry sweep's claim query no longer bounds \"RetryCount\". If the bound moved, move this guard "
            + "with it: RetrySweepWillTakeIt, IsDeferredPage and PageIsOnItsWay all promise a page is coming on "
            + "the strength of this query claiming the row, and nothing else in the build checks that it does.");

        var expression = bound.Groups["bound"].Value;

        if (expression.StartsWith('{'))
        {
            // Derived from the constant: the two cannot drift apart, whatever anybody sets it to.
            var symbol = expression.Trim('{', '}').Trim();

            Assert.True(
                symbol.EndsWith($"{nameof(Notification)}.{nameof(Notification.MaxRetries)}", StringComparison.Ordinal),
                $"the sweep's attempt bound is interpolated from '{symbol}', which is not "
                + $"{nameof(Notification)}.{nameof(Notification.MaxRetries)}. A SECOND constant is exactly the drift "
                + "this guard exists to stop — the sweep's window and the properties that promise a page is coming "
                + "must be bounded by ONE number. Interpolate Notification.MaxRetries directly.");
            return;
        }

        // A literal. Tolerated only while it happens to agree — and the moment somebody changes
        // MaxRetries, this is the thing that goes red instead of the pager going quiet.
        Assert.True(
            int.Parse(expression) == Notification.MaxRetries,
            $"the retry sweep's SQL claims rows with RetryCount < {expression}, but Notification.MaxRetries is "
            + $"{Notification.MaxRetries}. They have DRIFTED, and the drift is silent: rows between the two bounds "
            + "are reported by RetrySweepWillTakeIt / IsDeferredPage / PageIsOnItsWay as pages on their way — the "
            + "escalation counts the responder as reached and waits — while this query will not claim them. Nobody "
            + "is paged and nothing says so. Interpolate Notification.MaxRetries into the SQL instead of writing "
            + "the number twice.");
    }

    /// <summary>The reaper's attempt bound is the same constant, since it is the only thing that rescues a stranded row.</summary>
    [Fact]
    public void TheReapersAttemptBound_IsAlsoNotionOfMaxRetries_AndNobodyElses()
    {
        var bound = Regex.Match(ReaperCode(), @"RetryCount\s*<\s*(?<bound>[A-Za-z_][\w.]*|\d+)");

        Assert.True(bound.Success,
            "NotificationReaperBackgroundService no longer bounds RetryCount when it reclaims stuck "
            + "Sending/Retrying rows. The reaper is the ONLY thing that rescues a stranded in-flight page, "
            + "and PageIsOnItsWay promises a page is coming for such a row on the strength of the reaper "
            + "coming back for it. If the bound moved or was dropped, move this guard with it — nothing "
            + "else in the build checks that the reaper stops at the same place the sweep does.");

        var expression = bound.Groups["bound"].Value;

        if (!char.IsDigit(expression[0]))
        {
            // A symbol. It must be MaxRetries and nobody else's — a SECOND constant is exactly the drift
            // this guard exists to stop.
            Assert.True(
                expression.EndsWith($"{nameof(Notification)}.{nameof(Notification.MaxRetries)}", StringComparison.Ordinal),
                $"the reaper bounds RetryCount by '{expression}', which is not "
                + $"{nameof(Notification)}.{nameof(Notification.MaxRetries)}. The reaper, the retry sweep and "
                + "PageIsOnItsWay must all stop at ONE number; a second constant is a silent drift that leaves a "
                + "stranded page which no reaper reclaims yet PageIsOnItsWay still reports in flight. Reference "
                + "Notification.MaxRetries directly.");
            return;
        }

        // A bare literal. Tolerated only while it happens to agree — and the moment somebody changes
        // MaxRetries, this is the thing that goes red instead of the pager going quiet.
        Assert.True(
            int.Parse(expression) == Notification.MaxRetries,
            $"the reaper reclaims rows with RetryCount < {expression}, but Notification.MaxRetries is "
            + $"{Notification.MaxRetries}. They have DRIFTED: a Sending/Retrying row between the two bounds is one "
            + "the reaper will not reclaim and the sweep will not claim, while PageIsOnItsWay reports it as a page "
            + "on its way — the escalation counts the responder as reached and waits, and nobody is paged. Reference "
            + "Notification.MaxRetries instead of writing the number again.");
    }

    /// <summary>Whatever the sweep's soft-delete predicate refuses to claim, no liveness property may call a page in flight.</summary>
    [Fact]
    public void TheRetrySweepsSoftDeleteFilter_AndTheLivenessProperties_AgreeThatADeletedRowIsDead()
    {
        Assert.Matches(@"NOT\s+""IsDeleted""", ClaimSql());

        // Every status the sweep would otherwise take, and every status that means a send is under way.
        var statuses = StillComing.Concat(InFlight).Append(NotificationDeliveryStatus.Delivered);

        foreach (var status in statuses)
        {
            var deleted = new Notification
            {
                Type = NotificationType.VoiceCall,   // a channel that CAN page, so CountsAsReached is not vacuous
                DeliveryStatus = status,
                RetryCount = 0,
                NextRetryAt = DateTime.UtcNow.AddSeconds(60),
                IsDeleted = true
            };

            Assert.False(deleted.RetrySweepWillTakeIt,
                $"a soft-deleted {status} row claims the sweep will take it — the sweep's SQL says "
                + "NOT \"IsDeleted\" and will not");
            Assert.False(deleted.IsDeferredPage,
                $"a soft-deleted {status} row claims a postponed page is coming; nothing will send it");
            Assert.False(deleted.PageIsOnItsWay,
                $"a soft-deleted {status} row reports a page in flight, so a dedupe hit onto it counts a "
                + "responder as REACHED — while the sweep that was supposed to send it skips the row for good");
            Assert.False(deleted.CountsAsReached);
        }
    }

    /// <summary>The C# restatement of the bound is chained to the same constant as the SQL.</summary>
    [Fact]
    public void TheSweepsAttemptBound_AndTheClaimItMakesInCSharp_AreTheSameNumber()
    {
        var atTheBound = new Notification
        {
            DeliveryStatus = NotificationDeliveryStatus.Failed,
            RetryCount = Notification.MaxRetries
        };
        var oneBelow = new Notification
        {
            DeliveryStatus = NotificationDeliveryStatus.Failed,
            RetryCount = Notification.MaxRetries - 1
        };

        Assert.False(atTheBound.RetrySweepWillTakeIt, "a row that has spent MaxRetries attempts is not claimed");
        Assert.False(atTheBound.PageIsOnItsWay, "...so nothing may report a page as still coming for it");
        Assert.True(oneBelow.RetrySweepWillTakeIt, "a row with budget left IS claimed");
        Assert.True(oneBelow.PageIsOnItsWay);
    }

    // ── 2. a Skipped row has no deadline to come back on ──────────────────────────────────────────

    [Fact]
    public void MarkSkipped_LeavesNoRetryDeadline_EvenOnARowThatHadOne()
    {
        var notification = new Notification
        {
            DeliveryStatus = NotificationDeliveryStatus.Failed,
            RetryCount = 1,
            NextRetryAt = DateTime.UtcNow.AddMinutes(2)
        };

        notification.MarkSkipped("the incident is Resolved — no longer contacting responders");

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.IsSent, "nothing was SENT — a Skipped row must never look delivered");
    }

    // ── 3. it puts no page on its way ─────────────────────────────────────────────────────────────

    /// <summary>A status that means nothing is coming puts no page on its way.</summary>
    [Theory]
    [InlineData(NotificationDeliveryStatus.Skipped)]
    [InlineData(NotificationDeliveryStatus.PermanentlyFailed)]
    public void AStatusThatMeansNothingIsComing_PutsNoPageOnItsWay(NotificationDeliveryStatus status)
    {
        Assert.False(new Notification { DeliveryStatus = status }.PageIsOnItsWay);
    }

    /// <summary>The other direction: a refused page is still on its way, because the sweep owns the row.</summary>
    [Fact]
    public void TheStatusesThatMeanAPageIsStillComing_SaySo()
    {
        Assert.True(new Notification { DeliveryStatus = NotificationDeliveryStatus.Failed }.PageIsOnItsWay);

        foreach (var status in InFlight.Append(NotificationDeliveryStatus.Delivered))
            Assert.True(new Notification { DeliveryStatus = status }.PageIsOnItsWay);

        // A row nobody has touched — Pending with no deadline and no attempt — claims nothing. No
        // dispatcher leaves a row in this state, and the safe reading of one that turns up is that
        // nobody has been contacted.
        Assert.False(new Notification { DeliveryStatus = NotificationDeliveryStatus.Pending }.PageIsOnItsWay);
        Assert.Contains(NotificationDeliveryStatus.Pending, StillComing);
    }

    /// <summary>A postponed page is a page on its way, and the rule is about the row rather than the reason.</summary>
    [Fact]
    public void APostponedPage_IsAPageOnItsWay_WhicheverReasonPostponedIt()
    {
        foreach (var reason in new[] { "voice cooldown: recent call to same user", "no voice provider is registered" })
        {
            var notification = new Notification();
            notification.MarkDeferred(DateTime.UtcNow.AddSeconds(60), reason);

            Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
            Assert.True(notification.IsDeferredPage, reason);
            Assert.True(notification.RetrySweepWillTakeIt, reason);
            Assert.True(notification.PageIsOnItsWay, reason);
        }
    }

    /// <summary>A row that has spent its retry budget is not a page on its way, whatever its status looks like.</summary>
    [Fact]
    public void ARowWithNoRetryBudgetLeft_IsNotAPageOnItsWay()
    {
        var exhausted = new Notification
        {
            DeliveryStatus = NotificationDeliveryStatus.Pending,
            NextRetryAt = DateTime.UtcNow.AddSeconds(60),
            RetryCount = Notification.MaxRetries
        };

        Assert.False(exhausted.RetrySweepWillTakeIt);
        Assert.False(exhausted.IsDeferredPage);
        Assert.False(exhausted.PageIsOnItsWay);
    }

    // ── the guard against a new status quietly landing in the wrong bucket ────────────────────────

    /// <summary>Every delivery status is classified as coming or over, so a new one cannot default into a bucket.</summary>
    [Fact]
    public void EveryDeliveryStatus_IsClassified_AsComingOrOver()
    {
        var classified = StillComing.Concat(Over).Concat(InFlight).ToHashSet();

        var unclassified = Enum.GetValues<NotificationDeliveryStatus>()
            .Where(s => !classified.Contains(s))
            .ToList();

        Assert.True(unclassified.Count == 0,
            "New notification delivery status(es) with no decision recorded anywhere: "
            + string.Join(", ", unclassified)
            + ".\n\nDecide, here, what it means: is a page still coming for a row in this state (then the "
            + "retry sweep's SQL window must contain it, and PageIsOnItsWay must be honest about whether "
            + "anybody has been contacted yet), or is it over (then nothing may ever retry it, and no "
            + "escalation step may count it as having reached a human)?");
    }
}
