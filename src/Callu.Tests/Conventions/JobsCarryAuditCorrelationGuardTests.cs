namespace Callu.Tests.Conventions;

/// <summary>Every audit row a job run writes shares that run's correlation id.</summary>
// OpenAuditModel 1.0 treats the job run as the logical operation. A job that forgets the scope
// writes rows nothing ties together, and nobody notices until an auditor asks what one run did.
public class JobsCarryAuditCorrelationGuardTests
{
    [Fact]
    public void EveryQuartzJob_OpensTheRunsCorrelationScope()
    {
        var dir = Path.Combine(SourceScanner.Root().FullName, "Callu.Infrastructure", "Quartz");
        var jobs = Directory.EnumerateFiles(dir, "*QuartzJob.cs")
            .Where(f => File.ReadAllText(f).Contains("Task Execute(IJobExecutionContext"))
            .ToList();

        Assert.True(jobs.Count >= 16, $"found only {jobs.Count} jobs; the scan has lost them");

        var offenders = jobs
            .Where(f => !File.ReadAllText(f).Contains("AuditJobCorrelation.Begin(context)"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "no correlation scope in: " + string.Join(", ", offenders));
    }
}
