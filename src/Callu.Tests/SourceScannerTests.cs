namespace Callu.Tests;

/// <summary>Tests the mask every structural guard rests on, because a broken mask goes green rather than red.</summary>
public class SourceScannerTests
{
    /// <summary>
    /// The property every guard depends on: masking never moves anything. Offsets and line numbers
    /// in the masked text point at the same characters in the original.
    /// </summary>
    [Fact]
    public void Masking_PreservesLength_AndEveryLineBreak()
    {
        const string source = """
            var a = "hello";   // a comment
            /* block
               comment */
            var b = 'x';
            """;

        var masked = SourceScanner.Mask(source);

        Assert.Equal(source.Length, masked.Length);
        Assert.Equal(
            source.Select((c, i) => (c, i)).Where(x => x.c is '\n' or '\r').Select(x => x.i),
            masked.Select((c, i) => (c, i)).Where(x => x.c is '\n' or '\r').Select(x => x.i));
    }

    /// <summary>A brace inside a string literal is not a brace, in either direction.</summary>
    [Theory]
    [InlineData("""var s = "a } brace";""")]
    [InlineData("""var s = "a { brace";""")]
    [InlineData(""""var s = """a } raw brace"""; """")]
    [InlineData("""var s = @"a } verbatim brace";""")]
    [InlineData("""var c = '}';""")]
    [InlineData("var s = \"escaped \\\" quote and a } brace\";")]
    public void ABraceInsideALiteral_IsNotCode(string source)
    {
        var masked = SourceScanner.Mask(source);

        Assert.DoesNotContain('{', masked);
        Assert.DoesNotContain('}', masked);
    }

    [Fact]
    public void ABraceInsideAComment_IsNotCode()
    {
        const string source = """
            // TODO: set { DispatchGeneration } here
            /* and } here too */
            var x = 1;
            """;

        var masked = SourceScanner.Mask(source);

        Assert.DoesNotContain('{', masked);
        Assert.DoesNotContain('}', masked);
        Assert.Contains("var x = 1;", masked);
    }

    /// <summary>Prose about an invariant is not code that breaks it, or the guard gets weakened rather than obeyed.</summary>
    [Fact]
    public void ProseAboutAnInvariant_IsNotCodeThatBreaksIt()
    {
        const string source = """
            /// Always derive it: DispatchGeneration = NotificationPayload.GenerationFor(startedAt).
            logger.LogDebug("DispatchGeneration = {Generation}", generation);
            var real = payload.DispatchGeneration;
            """;

        var masked = SourceScanner.Mask(source);

        // The doc comment and the log message both spell out the assignment the guard hunts for.
        // Only the real read of the property survives.
        Assert.DoesNotContain("DispatchGeneration =", masked);
        Assert.Contains("var real = payload.DispatchGeneration;", masked);
    }

    /// <summary>Code survives masking intact — a mask that ate code would fail every guard, loudly.</summary>
    [Fact]
    public void CodeOutsideLiterals_IsUntouched()
    {
        const string source = """
            var payload = new NotificationPayload
            {
                IncidentId = incident.Id,
                DispatchGeneration = NotificationPayload.GenerationFor(incident.EscalationStartedAt)
            };
            """;

        Assert.Equal(source, SourceScanner.Mask(source));
    }

    /// <summary>A string inside an interpolation hole does not end the outer string early.</summary>
    [Fact]
    public void AStringInsideAnInterpolationHole_DoesNotEndTheStringEarly()
    {
        const string source = """"
            var s = $"{map["key"]} }";
            var after = new Thing { Name = "ok" };
            """";

        var masked = SourceScanner.Mask(source);

        // The stray brace lives inside the interpolated string; only the object initializer's braces
        // are code, and they balance.
        Assert.Equal(1, masked.Count(c => c == '{'));
        Assert.Equal(1, masked.Count(c => c == '}'));
        Assert.Contains("var after = new Thing", masked);
    }

    /// <summary>An apostrophe in prose must not swallow the code after it as a character literal.</summary>
    [Fact]
    public void AnApostropheInAComment_DoesNotEatTheNextLine()
    {
        const string source = """
            // the responder's phone
            var x = new Thing { A = 1 };
            """;

        var masked = SourceScanner.Mask(source);

        Assert.Contains("var x = new Thing { A = 1 };", masked);
    }

    [Fact]
    public void TheProductTree_IsActuallyFound()
    {
        var files = SourceScanner.ProductFiles().ToList();

        Assert.Contains(files, f => Path.GetFileName(f) == "EscalationOrchestrator.cs");
        Assert.DoesNotContain(files, f => Path.GetFileName(f) == "SourceScanner.cs");
    }

    [Fact]
    public void Migrations_CanBeExcluded()
    {
        Assert.Contains(SourceScanner.ProductFiles(), f => f.Contains("Migrations", StringComparison.Ordinal));
        Assert.DoesNotContain(
            SourceScanner.ProductFiles(includeMigrations: false),
            f => f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
