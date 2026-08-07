using System.Text.RegularExpressions;
using Callu.Domain.Enums;

namespace Callu.Tests.Conventions;

/// <summary>The audit action is the field an auditor filters on, so it has to be the real action.</summary>
// It used to be a free string that fell back to Updated when it did not parse, which stored eleven
// distinct escalation outcomes as "Updated" with the name buried in Description.
public class AuditActionGuardTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "solution root not found");
        return dir!.FullName;
    }

    private static IEnumerable<string> ProductionSources() =>
        new[] { "Callu.Infrastructure", "Callu.Api", "Callu.Application" }
            .Select(p => Path.Combine(SolutionRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void TheScanSeesTheCode()
    {
        Assert.True(ProductionSources().Count() > 100, "source scan found almost nothing");
    }

    /// <summary>The actor is never the empty string; an unattributed row answers nobody's question.</summary>
    // Where there genuinely is no user, the writer passes a "system:*" sentinel naming which one.
    [Fact]
    public void NoCallerPassesAnEmptyActor()
    {
        var offenders = new List<string>();

        foreach (var file in ProductionSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"LogAsync\(\s*string\.Empty\s*,", RegexOptions.Singleline))
                offenders.Add($"{Path.GetFileName(file)}: {Line(text, m.Index)}");
        }

        Assert.True(offenders.Count == 0,
            "LogAsync was called with string.Empty as the actor:\n  " + string.Join("\n  ", offenders)
            + "\n\nPass the real user id, or a \"system:<what>\" sentinel naming the unattended path.");
    }

    /// <summary>The row-writing statements a migration may not aim at the audit trail.</summary>
    // Shaping the table is allowed and unavoidable — a column, an index. Rewriting a row is not:
    // the hash covers the row's own values, so nothing can change them and still verify.
    private static readonly string[] RowWritingVerbs =
        ["UPDATE", "INSERT", "DELETE", "TRUNCATE", "MERGE", "DROP COLUMN"];

    internal static bool RewritesTheAuditTrail(string call, string statement)
    {
        if (!statement.Contains("AuditLogs", StringComparison.Ordinal)) return false;
        if (call is "UpdateData" or "DeleteData" or "InsertData") return true;

        return RowWritingVerbs.Any(verb =>
            statement.Contains(verb, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>No migration may rewrite the audit trail.</summary>
    // Action is inside the row hash, so an UPDATE against AuditLogs makes every upgraded install
    // report tampering the following night. There is no backfill, and no resealing tool.
    [Fact]
    public void NoMigrationTouchesTheAuditTrail()
    {
        var migrations = Path.Combine(SolutionRoot(), "Callu.Infrastructure", "Migrations");
        Assert.True(Directory.Exists(migrations), $"migrations folder not found at {migrations}");

        var files = Directory.EnumerateFiles(migrations, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "migration scan found nothing");

        var offenders = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?<call>Sql|UpdateData|DeleteData|InsertData)\s*\(", RegexOptions.Singleline))
            {
                var window = text.Substring(m.Index, Math.Min(1200, text.Length - m.Index));
                if (RewritesTheAuditTrail(m.Groups["call"].Value, window))
                    offenders.Add($"{Path.GetFileName(file)}: {m.Groups["call"].Value}(… AuditLogs …)");
            }
        }

        Assert.True(offenders.Count == 0,
            "a migration writes to the audit trail:\n  " + string.Join("\n  ", offenders)
            + "\n\nThe row hash covers Action, UserId and the values, so rewriting a sealed row breaks"
            + " the chain and the nightly verification reports tampering on every install that upgrades."
            + " History stays as it was written.");
    }

    /// <summary>The detector above, on the shapes it has to tell apart.</summary>
    // It reads a window of source rather than parsed SQL, so what it does and does not catch is
    // worth pinning down: shaping the table has to pass, and every way of rewriting a row has to fail.
    [Theory]
    [InlineData("Sql", "CREATE UNIQUE INDEX \"IX_AuditLogs_Sequence\" ON \"AuditLogs\" (\"Sequence\")", false)]
    [InlineData("Sql", "CREATE INDEX \"IX_AuditLogs_Sequence\" ON \"AuditLogs\" (\"Sequence\")", false)]
    [InlineData("Sql", "SELECT 1 FROM \"AuditLogs\" WHERE \"Sequence\" IS NOT NULL", false)]
    [InlineData("Sql", "UPDATE \"AuditLogs\" SET \"Action\" = 0", true)]
    [InlineData("Sql", "update \"AuditLogs\" set \"Summary\" = ''", true)]
    [InlineData("Sql", "DELETE FROM \"AuditLogs\"", true)]
    [InlineData("Sql", "TRUNCATE \"AuditLogs\"", true)]
    [InlineData("Sql", "ALTER TABLE \"AuditLogs\" DROP COLUMN \"Summary\"", true)]
    [InlineData("Sql", "UPDATE \"Notifications\" SET \"IsRead\" = true", false)]
    [InlineData("UpdateData", "table: \"AuditLogs\"", true)]
    [InlineData("InsertData", "table: \"AuditLogs\"", true)]
    [InlineData("UpdateData", "table: \"Notifications\"", false)]
    public void TheDetectorTellsShapingApartFromRewriting(string call, string statement, bool caught)
    {
        Assert.Equal(caught, RewritesTheAuditTrail(call, statement));
    }

    /// <summary>No caller may pass a string literal where the action belongs.</summary>
    [Fact]
    public void NoCallerPassesTheActionAsAString()
    {
        var offenders = new List<string>();

        foreach (var file in ProductionSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"LogAsync\(\s*(?:""[^""]*""|[^,]*?)\s*,\s*""(?<action>[^""]*)""", RegexOptions.Singleline))
                offenders.Add($"{Path.GetFileName(file)}: \"{m.Groups["action"].Value}\"");

            foreach (Match m in Regex.Matches(text, @"action:\s*""(?<action>[^""]*)"""))
                offenders.Add($"{Path.GetFileName(file)}: action: \"{m.Groups["action"].Value}\"");
        }

        Assert.True(offenders.Count == 0,
            "use an AuditAction member, not a string: " + string.Join(", ", offenders));
    }

    /// <summary>The service must not quietly relabel an action it does not recognise.</summary>
    [Fact]
    public void TheServiceHasNoFallbackThatRewritesTheAction()
    {
        var service = File.ReadAllText(
            Path.Combine(SolutionRoot(), "Callu.Infrastructure", "Services", "AuditLogService.cs"));

        Assert.DoesNotContain("Enum.TryParse", service, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditAction.Updated", service, StringComparison.Ordinal);
    }

    /// <summary>Existing rows store the numeric value, so a member may be appended but never renumbered.</summary>
    [Theory]
    [InlineData(AuditAction.Created, 1)]
    [InlineData(AuditAction.Updated, 2)]
    [InlineData(AuditAction.Deleted, 3)]
    [InlineData(AuditAction.Viewed, 4)]
    [InlineData(AuditAction.Login, 5)]
    [InlineData(AuditAction.Logout, 6)]
    [InlineData(AuditAction.PasswordChanged, 7)]
    [InlineData(AuditAction.RoleAssigned, 8)]
    [InlineData(AuditAction.RoleRemoved, 9)]
    [InlineData(AuditAction.SettingsChanged, 10)]
    public void TheOriginalMembersKeepTheirNumbers(AuditAction action, int expected)
    {
        Assert.Equal(expected, (int)action);
    }

    [Fact]
    public void EveryMemberHasItsOwnNumber()
    {
        var values = Enum.GetValues<AuditAction>().Select(v => (int)v).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    /// <summary>Every action the audit filter offers has to be one some code path actually writes.</summary>
    // The drift test on the frontend list checks that list against the enum, which cannot see
    // whether anything writes a value. Four lifecycle transitions were logged as Updated while the
    // screen offered Acknowledged, Resolved, Closed and Reopened as filters that matched nothing,
    // and an auditor could not tell an empty result from a broken one.
    [Fact]
    public void EveryOfferedActionIsWrittenSomewhere()
    {
        // Each entry is a promise to either wire the action up or stop offering it as a filter.
        var notWrittenYet = new HashSet<string>
        {
            nameof(AuditAction.Viewed),
            nameof(AuditAction.PasswordChanged),
            nameof(AuditAction.RoleRemoved),
        };

        var source = string.Concat(ProductionSources().Select(File.ReadAllText));

        var unwritten = Enum.GetNames<AuditAction>()
            .Where(name => !notWrittenYet.Contains(name))
            .Where(name => !source.Contains("AuditAction." + name, StringComparison.Ordinal))
            .ToList();

        Assert.True(unwritten.Count == 0,
            "These actions are offered as audit filters but no production code writes them, so the "
            + "filter returns an empty screen forever: "
            + string.Join(", ", unwritten)
            + ". Write the action where the act happens, or take it out of the enum.");
    }

    /// <summary>Each lifecycle method writes its own action, not a generic one.</summary>
    // Checked per method rather than across the file: Acknowledged is also written by the
    // maintenance-window path, so a file-wide search still finds it after the acknowledge endpoint
    // regresses to Updated — which is exactly the regression this guards.
    [Theory]
    [InlineData("AcknowledgeIncidentAsync", nameof(AuditAction.Acknowledged))]
    [InlineData("ResolveIncidentAsync", nameof(AuditAction.Resolved))]
    [InlineData("CloseIncidentAsync", nameof(AuditAction.Closed))]
    [InlineData("ReopenIncidentAsync", nameof(AuditAction.Reopened))]
    [InlineData("DeleteIncidentAsync", nameof(AuditAction.Deleted))]
    public void EachLifecycleMethodWritesItsOwnAction(string method, string action)
    {
        var body = MethodBody("IncidentService.cs", method);

        Assert.True(body.Contains("AuditAction." + action, StringComparison.Ordinal),
            $"{method} does not write AuditAction.{action}. An auditor filtering for {action} would "
            + "see an empty screen and could not tell that from there being nothing to show.");
    }

    /// <summary>Source of one method, from its signature to the start of the next one.</summary>
    // Brace matching would be exact but a following-signature scan is enough here and does not
    // need a parser; if it ever returns nothing the assertion below says so rather than passing.
    private static int Line(string text, int index) =>
        text.Take(index).Count(c => c == '\n') + 1;

    private static string MethodBody(string fileName, string method)
    {
        var path = ProductionSources().Single(f => Path.GetFileName(f) == fileName);
        var source = File.ReadAllText(path);

        var start = source.IndexOf($"public async Task {method}", StringComparison.Ordinal);
        if (start < 0) start = source.IndexOf($" {method}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{method} not found in {fileName}; repoint this guard.");

        var next = Regex.Match(source[(start + method.Length)..], @"\n    (public|private|internal) ");
        return next.Success
            ? source.Substring(start, method.Length + next.Index)
            : source[start..];
    }
}
