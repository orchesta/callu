using System.Text.RegularExpressions;
using Callu.Api.Configuration;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Callu.Tests.Conventions;

/// <summary>
/// The voice callback token is a bearer credential: whoever holds one can acknowledge the incident it
/// was minted for, and callu-voice presents no other credential of its own. It travels in a URL, and
/// three things this repository ships write URLs down by default — the bundled nginx access log, the
/// API's Serilog request log (a rolling file kept for a month), and that same request log at Error
/// level whenever the endpoint answers 5xx. Each is pinned here because two of them are not C#.
/// </summary>
public class CallbackTokenNeverReachesALogGuardTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "solution root not found");
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([SolutionRoot(), .. parts]));

    // ------------------------------------------------------------------ sink 1: nginx

    private static string Nginx() => Read("Callu.Web", "nginx.conf");

    /// <summary>The access-log format for /api/ writes the path and not the query, which is why the token is in the query.</summary>
    [Fact]
    public void TheApiAccessLogFormat_WritesThePathAndNotTheQuery()
    {
        var format = Regex.Match(Nginx(), @"log_format\s+no_query\s+(?<body>[^;]*);", RegexOptions.Singleline);

        Assert.True(format.Success, "the no_query log format is gone from nginx.conf");

        var body = format.Groups["body"].Value;

        Assert.Contains("$uri", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$request_uri", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$args", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$query_string", body, StringComparison.Ordinal);

        // "$request" is the whole request line, query included; "$request_method" is not.
        Assert.DoesNotContain("\"$request\"", body, StringComparison.Ordinal);
    }

    /// <summary>Every proxied API request is logged with that format, so nothing on the prefix falls back to the default.</summary>
    [Fact]
    public void EveryApiLocationUsesIt()
    {
        var locations = Regex.Matches(Nginx(), @"^\s*location\s+/api/\s*\{", RegexOptions.Multiline);

        Assert.NotEmpty(locations);
        Assert.All(
            locations.Select(m => Nginx()[m.Index..]),
            block => Assert.Contains("access_log /var/log/nginx/access.log no_query;",
                block[..block.IndexOf('}')], StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ sink 4: the nginx error log

    /// <summary>nginx has no format directive for the error log, so a failed upstream logs the request line, query and all, at "error" level.</summary>
    [Fact]
    public void TheApiLocationRaisesErrorLogAboveError()
    {
        var locations = Regex.Matches(Nginx(), @"^\s*location\s+/api/\s*\{", RegexOptions.Multiline);

        Assert.NotEmpty(locations);
        Assert.All(
            locations.Select(m => Nginx()[m.Index..]),
            block =>
            {
                var body = block[..block.IndexOf('}')];
                var errorLog = Regex.Match(body, @"error_log\s+\S+\s+(?<level>\w+)\s*;");

                Assert.True(errorLog.Success, "the /api/ location has no error_log directive");
                Assert.Contains(errorLog.Groups["level"].Value, new[] { "crit", "alert", "emerg" });
            });
    }

    // ------------------------------------------------------------------ sink 2 and 3: the request log

    /// <summary>The request log redacts whatever RequestPath turns out to hold.</summary>
    // It is the path alone until IncludeQueryInRequestPath is turned on, and the whole target after — and
    // that switch is one line away from a log kept for a month, written at Error for every 5xx. This runs
    // the exact function Program.cs wires in, rather than grepping for its name — a version that redacted
    // the wrong argument, or was never called, both used to leave this green.
    [Fact]
    public void TheRequestLogRedactsTheRequestTarget()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";

        var properties = RequestLoggingProperties.Get(
                context,
                $"/api/callu-voice/callback?{CalluVoiceCallbackTokenProtector.TokenQueryKey}=a-live-token",
                elapsedMs: 12.3,
                statusCode: 500)
            .ToList();

        var requestPath = Assert.Single(properties, p => p.Name == "RequestPath");
        var rendered = Assert.IsType<string>(((ScalarValue)requestPath.Value).Value);

        Assert.DoesNotContain("a-live-token", rendered, StringComparison.Ordinal);

        // And Program.cs has to actually assign this function on a LIVE line — not merely define one
        // nothing calls, and not a dead comment still naming it after the wiring was dropped.
        var liveLines = Read("Callu.Api", "Program.cs")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
        var program = string.Join('\n', liveLines);

        Assert.True(
            Regex.IsMatch(program, @"GetMessageTemplateProperties\s*=\s*(Callu\.Api\.Configuration\.)?RequestLoggingProperties\.Get"),
            "Program.cs no longer wires RequestLoggingProperties.Get into UseSerilogRequestLogging");
    }

    /// <summary>The redactor only knows the keys it is told about, and the token's key has to be one of them.</summary>
    [Fact]
    public void TheTokenParameterIsOneTheRedactorKnows()
    {
        var line = $"/api/callu-voice/callback?{CalluVoiceCallbackTokenProtector.TokenQueryKey}=a-live-token";

        Assert.DoesNotContain("a-live-token",
            SensitiveQueryRedactor.RedactTarget(line), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ and the traces

    /// <summary>The version of the ASP.NET Core instrumentation that redacts url.query by default is the one pinned.</summary>
    // Redaction has been on by default since 1.10; below that the tag carries the query as the client sent it.
    [Fact]
    public void TheTracingPackageRedactsTheQueryByDefault()
    {
        var csproj = Read("Callu.Infrastructure", "Callu.Infrastructure.csproj");

        var pinned = Regex.Match(
            csproj,
            @"OpenTelemetry\.Instrumentation\.AspNetCore""\s+Version=""(?<version>[0-9]+\.[0-9]+)");

        Assert.True(pinned.Success, "the ASP.NET Core tracing package is no longer pinned in Callu.Infrastructure.csproj");
        Assert.True(
            Version.Parse(pinned.Groups["version"].Value) >= new Version(1, 10),
            $"OpenTelemetry.Instrumentation.AspNetCore {pinned.Groups["version"].Value} exports url.query as the "
            + "client sent it, so the callback token would reach every span.");
    }

    /// <summary>Nothing shipped here turns that redaction off, or writes over the tag it produces.</summary>
    // Both are silent: the span still carries a url.query either way, and only its value says whether the
    // token is on it. Setting the tag is the louder of the two — it replaces a blanket redaction with
    // whatever the setter decided to keep.
    [Fact]
    public void NothingUndoesIt()
    {
        const string optOut = "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";

        var shipped = ShippedFiles().ToList();

        Assert.NotEmpty(shipped);

        // This file spells both of them out in order to forbid them; everywhere else they are the defect.
        var guard = $"{nameof(CallbackTokenNeverReachesALogGuardTests)}.cs";

        Assert.All(shipped.Where(f => Path.GetFileName(f) != guard), file =>
        {
            var text = File.ReadAllText(file);

            Assert.DoesNotContain(optOut, text, StringComparison.Ordinal);

            Assert.False(
                Regex.IsMatch(text, @"SetTag\s*\(\s*""url\.query"""),
                $"{file} sets the url.query tag, which discards the redaction the instrumentation applied to it.");
        });
    }

    /// <summary>Every file this repository ships that could carry either of those, node_modules and build output aside.</summary>
    private static IEnumerable<string> ShippedFiles()
    {
        var root = new DirectoryInfo(SolutionRoot());

        foreach (var file in Walk(root))
            yield return file;

        foreach (var file in root.Parent?.EnumerateFiles("*") ?? [])
        {
            if (file.Name.StartsWith("docker-compose", StringComparison.Ordinal)
                || file.Name.StartsWith(".env", StringComparison.Ordinal))
                yield return file.FullName;
        }
    }

    private static IEnumerable<string> Walk(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.Extension is ".cs" or ".json" or ".csproj" or ".props" or ".yml" or ".yaml" or ".conf")
                yield return file.FullName;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (child.Name is "bin" or "obj" or "node_modules" or ".git") continue;

            foreach (var file in Walk(child))
                yield return file;
        }
    }

    /// <summary>The path is what every one of those sinks writes, so the token may never be built into one.</summary>
    [Fact]
    public void TheAddressCalluBuilds_KeepsTheTokenOutOfThePath()
    {
        var built = new Uri(
            CalluVoiceCallbackTokenProtector.CallbackUrlFor("https://callu.example.com", "a-live-token"));

        Assert.DoesNotContain("a-live-token", built.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(CalluVoiceConfig.CallbackPath, built.AbsolutePath);
    }
}
