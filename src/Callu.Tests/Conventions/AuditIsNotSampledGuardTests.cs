using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>An audit row is written whether or not the trace it ran under was sampled.</summary>
// Tracing is a sampled diagnostic; auditing is a complete record. A trail that appears only for sampled
// requests is a sampled trail, and the gap is invisible until the missing row is the one that matters.
public class AuditIsNotSampledGuardTests
{
    /// <summary>The properties that answer "is this trace being recorded" — a question audit must not ask.</summary>
    private static readonly string[] SamplingChecks =
        ["IsAllDataRequested", "Recorded", "ActivityTraceFlags.Recorded"];

    private static readonly string[] AuditWritePath =
    [
        "Callu.Infrastructure/Services/AuditLogService.cs",
        "Callu.Infrastructure/Audit/AuditTraceContext.cs",
        "Callu.Infrastructure/Audit/AuditCorrelationScope.cs",
    ];

    [Fact]
    public void NoAuditWritePathAsksWhetherTheTraceIsBeingRecorded()
    {
        var offenders = new List<string>();

        foreach (var relative in AuditWritePath)
        {
            var path = Path.Combine(SourceScanner.Root().FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relative} has moved; this guard no longer covers it");

            var source = File.ReadAllText(path);
            offenders.AddRange(SamplingChecks
                .Where(check => Regex.IsMatch(source, $@"\b{Regex.Escape(check)}\b"))
                .Select(check => $"{Path.GetFileName(relative)} reads {check}; an audit row may not depend on sampling"));
        }

        Assert.Empty(offenders);
    }
}
