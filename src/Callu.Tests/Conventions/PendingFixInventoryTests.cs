using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>
/// Every skipped test stands for a tracked defect, in a shape a machine can read.
/// </summary>
public class PendingFixInventoryTests
{
    private sealed record PendingFix(string Slug, string Where, string Why);

    // Empty, and that is the state to keep it in.
    private static readonly PendingFix[] KnownPendingFixes = [];

    // Excluded from its own scan: its control groups are literal, malformed skip strings.
    private const string ThisFile = "PendingFixInventoryTests.cs";

    // A kebab-case slug rather than a tracker id: the skip reason has to mean something to a reader
    // who has only this repository.
    // The trailing lookahead keeps the closing quote out of the match, so the two detectors below
    // can be compared by offset.
    private static readonly Regex WellFormed = new(
        """Skip\s*=\s*"PENDING FIX (?<slug>[a-z0-9]+(?:-[a-z0-9]+){1,6}) — (?<reason>[^"]{20,})(?=")""",
        RegexOptions.Compiled);

    private static readonly Regex Claimed = new(
        """Skip\s*=\s*"(?<text>[^"]*PENDING FIX[^"]*)(?=")""",
        RegexOptions.Compiled);

    // RAW source, not SourceScanner.Code: this guard's subject IS a string literal, and masking
    // would blind it. A skip reason quoted in a comment therefore reads as a real skip, which only
    // ever adds an inventory obligation.
    private static IEnumerable<(string File, string Text)> TestSources() =>
        SourceScanner.Files(["Callu.Tests"])
            .Select(f => (File: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .Where(s => s.File != ThisFile);

    private static IEnumerable<(string File, string Slug)> DiscoveredPendingFixes() =>
        TestSources()
            .SelectMany(s => WellFormed.Matches(s.Text)
                .Select(m => (s.File, Slug: m.Groups["slug"].Value)));

    [Fact]
    public void EveryPendingFixSkip_MatchesTheDocumentedShape()
    {
        var malformed = new List<string>();

        foreach (var (file, text) in TestSources())
        {
            var wellFormedAt = WellFormed.Matches(text).Select(m => m.Index).ToHashSet();

            malformed.AddRange(Claimed.Matches(text)
                .Where(m => !wellFormedAt.Contains(m.Index))
                .Select(m => $"{file}: \"{m.Groups["text"].Value}\""));
        }

        Assert.True(malformed.Count == 0,
            "These skips claim to be pending fixes but do not carry a slug and a reason in the "
            + "documented shape — PENDING FIX <kebab-case-slug> — <one line>: "
            + string.Join("; ", malformed.Order(StringComparer.Ordinal))
            + ".\n\nThe shape is what makes the skip trackable. Without a slug that names the defect "
            + "nobody can tell what it is waiting on; without a reason nobody can tell whether it is "
            + "still waiting or simply forgotten.");
    }

    [Fact]
    public void EverySkippedTest_IsTrackedInTheInventory()
    {
        var tracked = KnownPendingFixes.Select(f => f.Slug).ToHashSet(StringComparer.Ordinal);

        var untracked = DiscoveredPendingFixes()
            .Where(x => !tracked.Contains(x.Slug))
            .Select(x => $"{x.Slug} ({x.File})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(untracked.Count == 0,
            "These tests are skipped for a defect that is not in KnownPendingFixes: "
            + string.Join(", ", untracked)
            + ".\n\nAdd an entry naming where in the product it lives and WHY it is not fixed yet. A "
            + "skipped test nobody is tracking is how a gate rots into decoration: invisible in a green "
            + "run, and nothing goes red when it goes stale.");
    }

    [Fact]
    public void EveryInventoryEntry_StillHasASkippedTestWaitingOnIt()
    {
        var live = DiscoveredPendingFixes().Select(x => x.Slug).ToHashSet(StringComparer.Ordinal);

        var stale = KnownPendingFixes
            .Where(f => !live.Contains(f.Slug))
            .Select(f => f.Slug)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "These defects are listed in KnownPendingFixes but no skipped test is waiting on them any "
            + "more: " + string.Join(", ", stale)
            + ".\n\nEither the fix landed and the test was un-skipped — in which case delete the entry, "
            + "because a done item on a to-do list costs the reader their trust — or the test was "
            + "deleted, in which case the defect just lost its only coverage.");
    }

    [Fact]
    public void EveryInventoryEntry_SaysWhereItLivesAndWhyItIsNotFixed()
    {
        Assert.All(KnownPendingFixes, fix =>
        {
            Assert.Matches(@"^[a-z0-9]+(?:-[a-z0-9]+){1,6}$", fix.Slug);

            Assert.False(string.IsNullOrWhiteSpace(fix.Where),
                $"{fix.Slug} does not say where in the product the defect lives");

            Assert.True(fix.Why.Length >= 60,
                $"{fix.Slug}'s reason is {fix.Why.Length} characters — too short to tell whoever picks "
                + "it up what the defect is and why it is still open. This array IS the to-do list.");
        });
    }

    [Fact]
    public void TheDockerSkip_IsNotMistakenForAPendingFix()
    {
        Assert.DoesNotContain("PENDING FIX", DockerAvailability.SkipReason, StringComparison.Ordinal);

        Assert.DoesNotContain(DiscoveredPendingFixes(), x => x.File == "PostgresHarness.cs");
    }

    [Theory]
    [InlineData("""[Fact(Skip = "PENDING FIX illegal-transition-is-500 — the durable fix is one arm in GlobalExceptionHandler")]""", true)]
    [InlineData("""[PostgresFact(Skip = "PENDING FIX step-level-not-derived — Level is copied from the request, never derived")]""", true)]
    [InlineData("""[Fact(Skip = "PENDING FIX illegal-transition-is-500")]""", false)]
    [InlineData("""[Fact(Skip = "PENDING FIX - hyphen, and the reason is on the wrong side")]""", false)]
    [InlineData("""[Fact(Skip = "PENDING FIX Illegal-Transition — capitals are not a kebab-case slug")]""", false)]
    [InlineData("""[Fact(Skip = "PENDING FIX A17 — a bare tracker id says nothing to a reader")]""", false)]
    [InlineData("""[Fact(Skip = "PENDING FIX transition — one word does not describe a defect")]""", false)]
    [InlineData("""[Fact(Skip = "pending fix illegal-transition-is-500 — lower case does not count as the documented shape")]""", false)]
    [InlineData("""[Fact(Skip = "PENDING FIX illegal-transition-is-500 — too short")]""", false)]
    public void TheShapeDetector_AcceptsOnlyTheDocumentedForm(string attribute, bool expected)
    {
        Assert.Equal(expected, WellFormed.IsMatch(attribute));
    }

    [Theory]
    [InlineData("""[Fact(Skip = "PENDING FIX illegal-transition-is-500")]""")]
    [InlineData("""[Fact(Skip = "PENDING FIX A17 — a bare tracker id says nothing to a reader")]""")]
    [InlineData("""[Fact(Skip = "PENDING FIX whatever")]""")]
    public void TheLooseDetector_CatchesAMalformedClaim(string attribute)
    {
        Assert.Matches(Claimed, attribute);
    }

    [Fact]
    public void TheScan_CanStillSeeTheTestTreeAndReadItsSkips()
    {
        var files = TestSources().Select(s => s.File).ToList();

        Assert.Contains("MechanismCoverageTests.cs", files);
        Assert.DoesNotContain(ThisFile, files);

        const string sample = """
            public class SomethingTests
            {
                [PostgresFact(Skip = "PENDING FIX expiry-batch-lost-to-one-conflict — a reason long enough to be worth reading")]
                public async Task TheInvariantHolds() { }

                [Fact]
                public void SomethingElse() { }
            }
            """;

        Assert.Equal(
            ["expiry-batch-lost-to-one-conflict"],
            WellFormed.Matches(sample).Select(m => m.Groups["slug"].Value));
    }
}
