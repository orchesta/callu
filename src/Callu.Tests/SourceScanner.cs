using System.Collections.Concurrent;

namespace Callu.Tests;

/// <summary>The one source scanner the structural guards share; <see cref="Code"/> reads code, not text.</summary>
internal static class SourceScanner
{
    /// <summary>The projects that ship. Test-only and generated code is not held to these invariants.</summary>
    public static readonly string[] ProductProjects =
        ["Callu.Api", "Callu.Application", "Callu.Domain", "Callu.Infrastructure", "Callu.Shared", "Callu.Worker"];

    /// <summary>Walks up from the test binaries to the directory holding the solution file.</summary>
    public static DirectoryInfo Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>Every product .cs file. <paramref name="includeMigrations"/> off for guards about hand-written code.</summary>
    public static IEnumerable<string> ProductFiles(bool includeMigrations = true) =>
        Files(ProductProjects, includeMigrations);

    /// <summary>The .cs files of the named projects (a subset of <see cref="ProductProjects"/>, or the test project).</summary>
    public static IEnumerable<string> Files(IEnumerable<string> projects, bool includeMigrations = true) =>
        projects
            .Select(p => Path.Combine(Root().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsUnder(f, "obj") && !IsUnder(f, "bin"))
            .Where(f => includeMigrations || !IsUnder(f, "Migrations"));

    private static bool IsUnder(string file, string directory) =>
        file.Contains($"{Path.DirectorySeparatorChar}{directory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>The file's code, with comments and literal contents blanked. Cached: the guards re-read the tree.</summary>
    public static string Code(string file) => Cache.GetOrAdd(file, f => Mask(File.ReadAllText(f)));

    /// <summary>Blanks comments and literals, keeping length and line breaks so offsets still line up.</summary>
    public static string Mask(string source)
    {
        var masked = source.ToCharArray();
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && Next(source, i) == '/')
            {
                var end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                Blank(masked, i, end);
                i = end;
            }
            else if (c == '/' && Next(source, i) == '*')
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? source.Length : close + 2;
                Blank(masked, i, end);
                i = end;
            }
            else if (c == '\'')
            {
                i = MaskCharLiteral(source, masked, i);
            }
            else if (StartsAString(source, i))
            {
                i = MaskString(source, masked, i);
            }
            else
            {
                i++;
            }
        }

        return new string(masked);
    }

    private static char Next(string source, int i) => i + 1 < source.Length ? source[i + 1] : '\0';

    /// <summary>Blanks [start, end) — newlines survive, so line numbers and offsets do not move.</summary>
    private static void Blank(char[] masked, int start, int end)
    {
        for (var i = start; i < end && i < masked.Length; i++)
            if (masked[i] is not ('\n' or '\r'))
                masked[i] = ' ';
    }

    /// <summary>A string starts at a quote, or at the <c>$</c> / <c>@</c> prefix run in front of one.</summary>
    private static bool StartsAString(string source, int i)
    {
        if (source[i] == '"') return true;
        if (source[i] is not ('$' or '@')) return false;

        var j = i;
        while (j < source.Length && source[j] is '$' or '@') j++;
        return j < source.Length && source[j] == '"';
    }

    private static int MaskCharLiteral(string source, char[] masked, int start)
    {
        var i = start + 1;
        while (i < source.Length)
        {
            if (source[i] == '\\') { i += 2; continue; }
            if (source[i] == '\'') { i++; break; }
            if (source[i] == '\n') break;   // not a char literal after all (an apostrophe in code we mis-read)
            i++;
        }

        Blank(masked, start, i);
        return Math.Max(i, start + 1);
    }

    /// <summary>Masks one string literal — regular, verbatim, interpolated, raw — and returns the index after it.</summary>
    private static int MaskString(string source, char[] masked, int start)
    {
        var i = start;
        var verbatim = false;
        var interpolated = false;

        while (i < source.Length && source[i] is '$' or '@')
        {
            verbatim |= source[i] == '@';
            interpolated |= source[i] == '$';
            i++;
        }

        var quotes = 0;
        while (i < source.Length && source[i] == '"') { quotes++; i++; }

        var end = quotes >= 3
            ? EndOfRawString(source, i, quotes)
            : quotes == 2
                ? i                                   // "" — an empty string, already consumed
                : EndOfQuotedString(source, masked, i, verbatim, interpolated);

        Blank(masked, start, end);
        return Math.Max(end, start + 1);
    }

    /// <summary>Raw string: runs to the next run of at least as many quotes.</summary>
    private static int EndOfRawString(string source, int i, int quotes)
    {
        while (i < source.Length)
        {
            if (source[i] != '"') { i++; continue; }

            var run = 0;
            while (i + run < source.Length && source[i + run] == '"') run++;

            if (run >= quotes) return i + run;
            i += run;
        }
        return source.Length;
    }

    /// <summary>Regular / verbatim / interpolated string, from just after the opening quote.</summary>
    private static int EndOfQuotedString(string source, char[] masked, int i, bool verbatim, bool interpolated)
    {
        var depth = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (!verbatim && c == '\\') { i += 2; continue; }
            if (!verbatim && c == '\n') return i;             // unterminated — stop at the line end

            if (interpolated && c == '{')
            {
                if (Next(source, i) == '{') { i += 2; continue; }   // {{ escape
                depth++;
                i++;
                continue;
            }

            if (interpolated && c == '}')
            {
                if (depth > 0) { depth--; i++; continue; }
                if (Next(source, i) == '}') { i += 2; continue; }    // }} escape
                i++;
                continue;
            }

            if (c == '"')
            {
                if (depth > 0)                                        // a string inside an interpolation hole
                {
                    i = MaskString(source, masked, i);
                    continue;
                }
                if (verbatim && Next(source, i) == '"') { i += 2; continue; }   // "" escape
                return i + 1;
            }

            i++;
        }

        return source.Length;
    }
}
