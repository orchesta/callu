using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>The Auditor role exists so nobody has to be made an Admin to read the audit log.</summary>
// A single Manage/Acknowledge/Resolve claim would undo that, and the frontend mirror deciding
// differently from the seeder would show buttons the API answers with 403.
public class AuditorRoleGuardTests
{
    private static readonly string SeederSource = SourceFile(
        "Callu.Infrastructure", "Persistence", "Seeding", "DbSeeder.cs");

    private static readonly string FrontendSource = SourceFile(
        "Callu.Web", "src", "shared", "auth", "roles.ts");

    private static string SourceFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "solution root not found");
        var path = Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"missing: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The claim list a role is seeded with, read out of the seeder itself.</summary>
    private static IReadOnlyList<string> SeededClaims(string role)
    {
        var block = Regex.Match(SeederSource, $@"\[""{role}""\]\s*=\s*(?:new\[\])?\s*[\[{{](?<body>[^\]}}]*)[\]}}]");
        Assert.True(block.Success, $"'{role}' block not found in the seeder");

        return Regex.Matches(block.Groups["body"].Value, @"""(?<claim>Can\w+)""")
            .Select(m => m.Groups["claim"].Value)
            .ToList();
    }

    private static IReadOnlyList<string> FrontendClaims(string role)
    {
        var block = Regex.Match(FrontendSource, $@"\b{role}:\s*\[(?<body>[^\]]*)\]");
        Assert.True(block.Success, $"'{role}' block not found in the frontend mirror");

        return Regex.Matches(block.Groups["body"].Value, @"PERMISSIONS\.(?<name>\w+)")
            .Select(m => m.Groups["name"].Value)
            .ToList();
    }

    [Fact]
    public void TheSeederKnowsTheRole()
    {
        Assert.Contains("\"Auditor\"", SeederSource, StringComparison.Ordinal);
        Assert.NotEmpty(SeededClaims("Auditor"));
    }

    [Fact]
    public void AnAuditor_CanReadTheAuditLog()
    {
        Assert.Contains("CanViewAuditLog", SeededClaims("Auditor"));
    }

    [Fact]
    public void AnAuditor_ChangesNothing()
    {
        var offenders = SeededClaims("Auditor")
            .Where(c => c.StartsWith("CanManage", StringComparison.Ordinal)
                        || c is "CanAcknowledgeIncidents" or "CanResolveIncidents")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"an auditor that can change things is not an auditor: {string.Join(", ", offenders)}");
    }

    /// <summary>An auditor has to be able to see what the audit entries refer to.</summary>
    [Fact]
    public void AnAuditor_SeesEverythingAViewerSees()
    {
        var missing = SeededClaims("Viewer").Except(SeededClaims("Auditor")).ToList();

        Assert.True(missing.Count == 0, $"auditor is missing viewer claims: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("TeamLead")]
    [InlineData("Member")]
    [InlineData("Viewer")]
    [InlineData("Auditor")]
    public void TheFrontendMirror_MatchesTheSeeder(string role)
    {
        var seeded = SeededClaims(role).Order().ToList();
        // The mirror names permissions without the Can prefix.
        var mirrored = FrontendClaims(role).Select(n => "Can" + n).Order().ToList();

        Assert.Equal(seeded, mirrored);
    }

    /// <summary>Team scoping has to exempt the Auditor, or the claim above buys nothing.</summary>
    // An auditor belongs to no team by design, so a scope of "your teams plus the unassigned"
    // resolves to almost nothing. The symptom is not an error: the trail lists rows referring to
    // incidents that 404 for the one person whose job is to read them.
    [Theory]
    [InlineData("IncidentService.cs")]
    [InlineData("IncidentNoteService.cs")]
    public void TeamScopingExemptsTheAuditor(string fileName)
    {
        var source = SourceFile("Callu.Infrastructure", "Services", fileName);

        var adminExemptions = source.Split("IsInRole(\"Admin\")").Length - 1;
        var auditorExemptions = source.Split("IsInRole(\"Auditor\")").Length - 1;

        Assert.True(adminExemptions > 0, $"{fileName} no longer exempts Admin; repoint this guard.");
        Assert.True(auditorExemptions >= adminExemptions,
            $"{fileName} exempts Admin from team scoping {adminExemptions} time(s) but Auditor only "
            + $"{auditorExemptions}. An auditor is in no team, so every incident read returns nothing "
            + "and the audit trail points at records its reader cannot open.");
    }
}
