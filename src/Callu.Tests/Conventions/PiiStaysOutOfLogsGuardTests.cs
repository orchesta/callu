using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>A log line that names a person carries a shortened identifier, never the address itself.</summary>
// The file log is kept for a month and gets pasted into issues; an address in it leaves with the paste.
public class PiiStaysOutOfLogsGuardTests
{
    /// <summary>Placeholders whose argument is somebody's address or number.</summary>
    private static readonly string[] PersonalPlaceholders =
        ["{Email}", "{Recipient}", "{To}", "{Phone}", "{PhoneNumber}"];

    private static readonly Regex LogCall = new(
        @"_?logger\s*\.\s*Log(Trace|Debug|Information|Warning|Error|Critical)\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryLogLineNamingAPerson_RedactsWhatItNames()
    {
        var offenders = new List<string>();

        foreach (var file in SourceScanner.ProductFiles(includeMigrations: false))
        {
            // Raw source, not SourceScanner.Code: that blanks string literals, and the placeholder
            // this guard looks for lives inside one.
            var code = File.ReadAllText(file);

            foreach (Match call in LogCall.Matches(code))
            {
                var text = call.Value;

                if (!PersonalPlaceholders.Any(p => text.Contains(p, StringComparison.Ordinal))) continue;
                if (text.Contains("PiiRedactor.", StringComparison.Ordinal)) continue;

                var placeholder = PersonalPlaceholders.First(p => text.Contains(p, StringComparison.Ordinal));
                offenders.Add(
                    $"{Path.GetFileName(file)} logs {placeholder} without PiiRedactor: "
                    + Regex.Replace(text, @"\s+", " ")[..Math.Min(140, Regex.Replace(text, @"\s+", " ").Length)]);
            }
        }

        Assert.True(offenders.Count == 0,
            "These log lines put a personal identifier in the log verbatim. Wrap the argument in "
            + $"PiiRedactor.Email(...) or PiiRedactor.Phone(...):{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The redactor keeps enough to tell two people apart and drops the rest.</summary>
    [Fact]
    public void RedactionIsShortening_NotDeletion()
    {
        var first = Callu.Shared.Logging.PiiRedactor.Email("alice@example.com");
        var second = Callu.Shared.Logging.PiiRedactor.Email("bob@example.com");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("alice", first, StringComparison.Ordinal);
        Assert.Contains("example.com", first, StringComparison.Ordinal);
    }
}
