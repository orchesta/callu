using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>A comment may only cite something a reader of this repository can open.</summary>
public class CommentReferenceGuardTests
{
    private static readonly string[] ScannedExtensions =
        [".cs", ".ts", ".tsx", ".js", ".md", ".conf", ".yml", ".yaml", ".example", ".sh"];

    // Files a reader sees that carry no extension of their own.
    private static readonly string[] ScannedNames = ["Dockerfile"];

    private static readonly string[] SkippedDirectories =
        [".git", ".vs", "bin", "obj", "node_modules", "dist", "coverage"];

    // This file spells the forbidden shapes out, so it is the one file excluded from its own scan.
    private const string ThisFile = "CommentReferenceGuardTests.cs";

    private static readonly string[] ForeignDocRoots =
        ["code-notes", "docs/decisions", "docs/reviews", "docs\\decisions", "docs\\reviews", "mvp/", "mvp\\"];

    private static readonly Regex TrackerId = new(
        @"\(\s*[A-Z]\d{1,2}(?:\s*[,;/]\s*[A-Z]?\d{1,2})*\s*\)|\bFix \d\d\.[A-Z0-9]",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownLink = new(
        @"\]\((?<target>[^)\s]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownFile = new(
        @"(?<path>[\w][\w./\\-]*\.md)\b",
        RegexOptions.Compiled);

    [Fact]
    public void NoFile_PointsAtADocumentOutsideThisRepository()
    {
        var offenders = Sources()
            .SelectMany(file => Lines(file)
                .SelectMany(line => ForeignDocRoots
                    .Where(root => line.Text.Contains(root, StringComparison.OrdinalIgnoreCase))
                    .Select(root => $"{file.Relative}:{line.Number} cites `{root}`")))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These lines name a document that is not in this repository:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nCallu is public and those documents are not. A pointer a reader cannot follow is "
            + "worse than no pointer: say the reason in one line here, or say nothing.");
    }

    [Fact]
    public void NoFile_CitesAnOpaqueTrackerId()
    {
        var offenders = Sources()
            .SelectMany(file => Lines(file)
                .SelectMany(line => TrackerId.Matches(line.Text)
                    .Select(m => $"{file.Relative}:{line.Number} cites `{m.Value.Trim()}`")))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These lines cite an id that resolves nowhere in this repository:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nNobody outside the authors' own tracker can look one up. Describe the defect in "
            + "the comment's own words instead, or drop the comment.");
    }

    [Fact]
    public void EveryMarkdownLinkAndDocumentPath_ResolvesToAFileThatShipsHere()
    {
        var offenders = Sources()
            .SelectMany(file => Lines(file).SelectMany(line => References(line.Text, IsMarkdown(file.Path))
                .Where(reference => !Resolves(file.Path, reference))
                .Select(reference => $"{file.Relative}:{line.Number} → {reference}")))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These references do not resolve to a file in this repository:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nEither add the file, fix the path, or remove the reference.");
    }

    [Theory]
    [InlineData("// Reasoning: mvp/docs/code-notes/web-shared.md (D21).", true)]
    [InlineData("// see code-notes/api.md for the rationale", true)]
    [InlineData("/// <summary>Clamped before the write (Y1).</summary>", true)]
    [InlineData("# above the provider AttemptTimeout (15s). Fix 10.P0-3.", true)]
    [InlineData("// the hostNetwork comment on the Deployment (D24)", true)]
    [InlineData("// Clamped before the write, or the commit throws and escalation stays stuck.", false)]
    [InlineData("// Retries are bounded (see the resilience handler).", false)]
    [InlineData("// A 30s window, matching the retry job cadence.", false)]
    [InlineData("/// <summary>The projects that ship.</summary>", false)]
    public void TheDetector_FlagsAPointerAReaderCannotFollow_AndNothingElse(string line, bool flagged)
    {
        var caught = ForeignDocRoots.Any(root => line.Contains(root, StringComparison.OrdinalIgnoreCase))
                     || TrackerId.IsMatch(line);

        Assert.Equal(flagged, caught);
    }

    [Fact]
    public void TheReferenceReader_TakesRelativePathsAndLeavesUrlsAlone()
    {
        Assert.Equal(["CONTRIBUTING.md"], References("See [the guide](CONTRIBUTING.md) first.", isMarkdown: true));
        Assert.Equal(["../../LICENSE"], References("MIT — see [LICENSE](../../LICENSE).", isMarkdown: true));
        Assert.Equal(["docs/timezone-design.md"], References("/// See docs/timezone-design.md.", isMarkdown: false));
        Assert.Empty(References("[upstream](https://example.com/tables.sql)", isMarkdown: true));
        Assert.Empty(References("[a section](#configuration)", isMarkdown: true));
        Assert.Empty(References("/// API: https://github.com/verimor/SMS-API/blob/master/user_guide.md", isMarkdown: false));
        Assert.Empty(References(@"const m = /Roles\s*=\s*[[{]([^\]}]*)[\]}]/.exec(source);", isMarkdown: false));
    }

    [Fact]
    public void TheScan_ReadsTheTreeItClaimsTo()
    {
        var files = Sources().ToList();

        Assert.Contains(files, f => f.Relative.EndsWith("Callu.Domain/Entities/Incident.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Relative.EndsWith("Callu.Web/src/shared/api/client.ts", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Relative == "README.md");
        Assert.Contains(files, f => f.Relative.EndsWith("Voximplant/Scripts/callu-conference.js", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Relative == "src/Callu.Web/.env.example");
        Assert.Contains(files, f => f.Relative.EndsWith("Callu.Api/Dockerfile", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Relative.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Relative.EndsWith(ThisFile, StringComparison.Ordinal));
    }

    private static bool IsMarkdown(string file) =>
        Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> References(string text, bool isMarkdown)
    {
        if (isMarkdown)
        {
            foreach (Match link in MarkdownLink.Matches(text))
            {
                var target = link.Groups["target"].Value;
                if (IsLocal(target)) yield return Anchorless(target);
            }
        }

        foreach (Match file in MarkdownFile.Matches(text))
        {
            var path = file.Groups["path"].Value;

            if (!IsLocal(path)) continue;
            if (PartOfAUrl(text, file.Index)) continue;
            if (text.Contains($"]({path}", StringComparison.Ordinal)) continue;

            yield return path;
        }
    }

    private static bool PartOfAUrl(string text, int matchIndex)
    {
        var start = matchIndex;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not ('(' or '"' or '\'')) start--;

        return text[start..matchIndex].Contains("://", StringComparison.Ordinal);
    }

    private static bool IsLocal(string target) =>
        !target.StartsWith('#')
        && !target.Contains("://", StringComparison.Ordinal)
        && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string Anchorless(string target)
    {
        var anchor = target.IndexOf('#');
        return anchor < 0 ? target : target[..anchor];
    }

    private static bool Resolves(string fromFile, string reference)
    {
        var candidate = reference.Replace('\\', '/').TrimEnd('/');
        if (candidate.Length == 0) return true;

        var roots = new[] { Path.GetDirectoryName(fromFile)!, SourceScanner.Root().FullName, RepositoryRoot().FullName };

        return roots.Any(root =>
        {
            var full = Path.GetFullPath(Path.Combine(root, candidate));
            return File.Exists(full) || Directory.Exists(full);
        });
    }

    private static DirectoryInfo RepositoryRoot() => SourceScanner.Root().Parent!;

    private static IEnumerable<(string Path, string Relative)> Sources()
    {
        var root = RepositoryRoot();

        return Directory
            .EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(f => ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                        || ScannedNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !SkippedDirectories.Any(d => IsUnder(f, d)))
            .Where(f => Path.GetFileName(f) != ThisFile)
            .Select(f => (f, Path.GetRelativePath(root.FullName, f).Replace('\\', '/')))
            .OrderBy(f => f.Item2, StringComparer.Ordinal);
    }

    private static bool IsUnder(string file, string directory) =>
        file.Contains($"{Path.DirectorySeparatorChar}{directory}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(int Number, string Text)> Lines((string Path, string Relative) file) =>
        File.ReadAllLines(file.Path).Select((text, i) => (i + 1, text));
}
