using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Two Worker invariants that live across three files: cron pinning and stop-grace ordering.</summary>
// Reads raw text, not SourceScanner.Code — both checks are about string literals, which the mask blanks.
public class WorkerScheduleAndShutdownGuardTests
{
    private static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([SourceScanner.Root().FullName, .. relativePath]));

    private static string ComposeFile() =>
        File.ReadAllText(Path.Combine(SourceScanner.Root().Parent!.FullName, "docker-compose.yml"));

    // ── A daily cron must not sit in a zone the operator can move under it ─────────────────────────

    private static readonly Regex CronTrigger = new(
        @"WithCronSchedule\(""(?<cron>[^""]+)""(?<rest>[^)]*)\)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryCronThatRunsOnceADayOrRarer_IsPinnedToUtc()
    {
        var source = Read("Callu.Worker", "Quartz", "WorkerQuartzExtensions.cs");
        var matches = CronTrigger.Matches(source);

        Assert.NotEmpty(matches);

        var unpinned = matches
            .Where(m => RunsAtMostDaily(m.Groups["cron"].Value))
            .Where(m => !m.Groups["rest"].Value.Contains("TimeZoneInfo.Utc", StringComparison.Ordinal))
            .Select(m => m.Groups["cron"].Value)
            .ToList();

        Assert.True(unpinned.Count == 0,
            "These daily-or-rarer cron triggers are not pinned to UTC: " + string.Join(", ", unpinned)
            + ".\n\nAn operator who sets TZ on the container (common, for readable logs) moves them, "
            + "and in a DST zone the transition day runs them twice or not at all. Add "
            + "`x => x.InTimeZone(TimeZoneInfo.Utc)`.");
    }

    [Fact]
    public void TheCronReader_TellsDailyApartFromSubMinute()
    {
        Assert.True(RunsAtMostDaily("0 0 2 * * ?"));
        Assert.True(RunsAtMostDaily("0 30 4 * * ?"));

        Assert.False(RunsAtMostDaily("0/10 * * * * ?"));
        Assert.False(RunsAtMostDaily("0 * * * * ?"));
        Assert.False(RunsAtMostDaily("0 0 * * * ?"));
    }

    /// <summary>Quartz cron is second-first: fixed second AND fixed minute AND fixed hour.</summary>
    private static bool RunsAtMostDaily(string cron)
    {
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return fields.Length >= 3 && fields.Take(3).All(f => int.TryParse(f, out _));
    }

    // ── A job nobody schedules is a job that never runs ───────────────────────────────────────────

    /// <summary>Every Quartz job in Infrastructure is scheduled on the Worker.</summary>
    // A job class compiles, is covered by tests, and does nothing in production if no trigger names
    // it — and the only symptom of that is the work silently not happening.
    [Fact]
    public void EveryQuartzJob_IsScheduledOnTheWorker()
    {
        var registration = Read("Callu.Worker", "Quartz", "WorkerQuartzExtensions.cs");

        var jobs = Directory
            .EnumerateFiles(
                Path.Combine(SourceScanner.Root().FullName, "Callu.Infrastructure", "Quartz"),
                "*QuartzJob.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        Assert.NotEmpty(jobs);

        var unscheduled = jobs
            .Where(job => !registration.Contains($"ScheduleJob<{job}>", StringComparison.Ordinal))
            .ToList();

        Assert.True(unscheduled.Count == 0,
            "These Quartz jobs exist but nothing on the Worker schedules them: "
            + string.Join(", ", unscheduled)
            + ".\n\nJobs run on the Worker and only there, so a job with no trigger never runs at all.");
    }

    // ── Docker's stop grace has to outlast the host's own shutdown budget ─────────────────────────

    [Fact]
    public void TheWorkersStopGracePeriod_OutlastsTheHostShutdownTimeout()
    {
        var shutdown = HostShutdownTimeoutSeconds();
        var grace = WorkerStopGracePeriodSeconds();

        Assert.True(grace > shutdown,
            $"docker-compose gives callu-worker {grace}s to stop, but the host waits up to "
            + $"{shutdown}s for Quartz jobs to finish (HostOptions.ShutdownTimeout). Docker SIGKILLs "
            + "first, so WaitForJobsToComplete is decoration and every upgrade cuts a job in half — "
            + "including between a page being dispatched and its outcome being written.");
    }

    [Fact]
    public void TheHostShutdownTimeout_OutlastsOneProviderAttempt()
    {
        var attempt = ProviderAttemptTimeoutSeconds();

        Assert.True(HostShutdownTimeoutSeconds() > attempt,
            $"a single non-idempotent provider attempt is allowed {attempt}s, so a shutdown budget at "
            + "or below that cannot let an in-flight page finish and record its outcome.");
    }

    private static int HostShutdownTimeoutSeconds()
    {
        var match = Regex.Match(
            Read("Callu.Worker", "Program.cs"),
            @"ShutdownTimeout\s*=\s*TimeSpan\.FromSeconds\((?<seconds>\d+)\)");

        Assert.True(match.Success,
            "Callu.Worker/Program.cs no longer sets HostOptions.ShutdownTimeout, so the shutdown "
            + "budget is back to an invisible framework default (30s) that nothing here pins.");

        return int.Parse(match.Groups["seconds"].Value);
    }

    private static int WorkerStopGracePeriodSeconds()
    {
        var compose = ComposeFile();
        var worker = compose.IndexOf("callu-worker:", StringComparison.Ordinal);
        Assert.True(worker >= 0, "docker-compose.yml has no callu-worker service.");

        var match = Regex.Match(compose[worker..], @"stop_grace_period:\s*(?<seconds>\d+)s");

        Assert.True(match.Success,
            "callu-worker has no stop_grace_period, so Docker falls back to 10s and SIGKILLs the one "
            + "host that pages while its jobs are still running.");

        return int.Parse(match.Groups["seconds"].Value);
    }

    private static int ProviderAttemptTimeoutSeconds()
    {
        var match = Regex.Match(
            Read("Callu.Infrastructure", "DI", "CommunicationModule.cs"),
            @"AttemptTimeout\s*=\s*new HttpTimeoutStrategyOptions\s*\{\s*Timeout\s*=\s*TimeSpan\.FromSeconds\((?<seconds>\d+)\)");

        Assert.True(match.Success, "ConfigureNonIdempotentProvider no longer sets an AttemptTimeout.");

        return int.Parse(match.Groups["seconds"].Value);
    }
}
