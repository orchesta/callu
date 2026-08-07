using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Method bodies, by name, out of a product source file. A regex, not a parser.</summary>
internal static class SourceMembers
{
    // Signature must end the line, which keeps expression-bodied members and properties out. The
    // lookahead skips type declarations, so a multi-line primary constructor cannot swallow the
    // first method after it.
    private static readonly Regex MethodSignature = new(
        @"^[ \t]*(?:public|private|protected|internal)(?![^\r\n]*\b(?:class|record|struct|interface|enum)\b)[^\r\n;=]*?\b(?<name>\w+)\s*\([^;]*?\)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static string Read(string fileName) =>
        SourceScanner.Code(
            SourceScanner.ProductFiles(includeMigrations: false)
                .Single(f => Path.GetFileName(f) == fileName));

    public static Dictionary<string, string> MethodBodies(string code)
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match signature in MethodSignature.Matches(code))
        {
            var open = code.IndexOf('{', signature.Index + signature.Length);
            if (open < 0) continue;

            var depth = 0;
            var i = open;
            for (; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) break;
            }

            var name = signature.Groups["name"].Value;
            var body = code[open..Math.Min(i + 1, code.Length)];

            if (!bodies.TryGetValue(name, out var existing) || body.Length > existing.Length)
                bodies[name] = body;
        }

        return bodies;
    }
}

public class SourceMembersTests
{
    [Fact]
    public void ABodyEndsWithItsOwnMethod_NotTheNextOne()
    {
        const string code = """
            public void First(string a)
            {
                if (a is null) { return; }
                Do(a);
            }

            public void Second()
            {
                Boom();
            }
            """;

        var bodies = SourceMembers.MethodBodies(code);

        Assert.Contains("Do(a);", bodies["First"]);
        Assert.DoesNotContain("Boom();", bodies["First"]);
        Assert.Contains("Boom();", bodies["Second"]);
    }

    [Fact]
    public void ABraceInAStringLiteral_DoesNotEndTheBody()
    {
        const string code = """"
            public void Only(int page)
            {
                var s = "a } brace";
                var clamped = Math.Clamp(page, 1, 100);
            }
            """";

        var bodies = SourceMembers.MethodBodies(SourceScanner.Mask(code));

        Assert.Contains("Math.Clamp(page, 1, 100)", bodies["Only"]);
    }

    [Fact]
    public void AMultiLineSignature_IsStillFound()
    {
        const string code = """
            public async Task<IActionResult> GetAll(
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 25,
                CancellationToken ct = default)
            {
                return Ok(page + pageSize);
            }
            """;

        Assert.Contains("return Ok(page + pageSize);", SourceMembers.MethodBodies(code)["GetAll"]);
    }

    [Fact]
    public void APrimaryConstructorClass_DoesNotSwallowItsFirstMethod()
    {
        const string code = """
            public class ServicesController(
                IServiceManagementService serviceManagement,
                INotificationPushService pushService) : ControllerBase
            {
                public async Task<IActionResult> GetAll([FromQuery] int page = 1, CancellationToken ct = default)
                {
                    return Ok(Math.Clamp(page, 1, 100));
                }
            }
            """;

        var bodies = SourceMembers.MethodBodies(code);

        Assert.Contains("GetAll", bodies.Keys);
        Assert.Contains("Math.Clamp(page, 1, 100)", bodies["GetAll"]);
        Assert.DoesNotContain("ServicesController", bodies.Keys);
    }

    [Fact]
    public void TheRealProductFiles_AreReadable()
    {
        var bodies = SourceMembers.MethodBodies(SourceMembers.Read("IncidentService.cs"));

        Assert.Contains("AcknowledgeIncidentAsync", bodies.Keys);
        Assert.Contains("ResolveIncidentAsync", bodies.Keys);
    }
}
