using Callu.Shared.Audit;

namespace Callu.Tests.Audit;

// Vectors from RFC 8785. An external verifier re-derives the digest from the bytes it received, so
// a canonicalizer that is merely self-consistent still fails against every other implementation.
public class JsonCanonicalizerTests
{
    [Fact]
    public void SortsMembersByUtf16CodeUnit_NotByLocale()
    {
        const string input = """
            {
              "peach": "This sorting order",
              "péché": "is wrong according to French",
              "pêche": "but canonicalization MUST",
              "sin":   "ignore locale"
            }
            """;

        var canonical = JsonCanonicalizer.Canonicalize(input);

        Assert.Equal(
            """{"peach":"This sorting order","péché":"is wrong according to French","pêche":"but canonicalization MUST","sin":"ignore locale"}""",
            canonical);
    }

    [Fact]
    public void SortsNestedObjectsToo()
    {
        var canonical = JsonCanonicalizer.Canonicalize("""{"b":{"d":1,"c":2},"a":3}""");
        Assert.Equal("""{"a":3,"b":{"c":2,"d":1}}""", canonical);
    }

    [Fact]
    public void KeepsArrayOrder()
    {
        var canonical = JsonCanonicalizer.Canonicalize("""{"a":[3,1,2]}""");
        Assert.Equal("""{"a":[3,1,2]}""", canonical);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("1.0", "1")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1e30", "1e+30")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("1e21", "1e+21")]
    [InlineData("1e20", "100000000000000000000")]
    public void FormatsNumbersTheWayEcmaScriptDoes(string input, string expected)
    {
        var canonical = JsonCanonicalizer.Canonicalize($$"""{"n":{{input}}}""");
        Assert.Equal($$"""{"n":{{expected}}}""", canonical);
    }

    [Fact]
    public void UsesShortEscapesWhereTheSpecDefinesThem()
    {
        var canonical = JsonCanonicalizer.Canonicalize("{\"a\":\"A\\\"\\\\\\n\\t\"}");
        Assert.Equal("{\"a\":\"A\\\"\\\\\\n\\t\"}", canonical);
    }

    [Fact]
    public void EscapesOtherControlCharactersAsFourHexDigits()
    {
        var canonical = JsonCanonicalizer.Canonicalize("{\"a\":\"x\\u001fy\"}");
        Assert.Equal("{\"a\":\"x\\u001fy\"}", canonical);
    }

    [Fact]
    public void LeavesNonAsciiUnescaped()
    {
        var canonical = JsonCanonicalizer.Canonicalize("""{"a":"Şahin — Öztürk"}""");
        Assert.Equal("""{"a":"Şahin — Öztürk"}""", canonical);
    }

    [Fact]
    public void DropsInsignificantWhitespace()
    {
        var canonical = JsonCanonicalizer.Canonicalize("{\n  \"a\" : 1 ,\n  \"b\" : null\n}");
        Assert.Equal("""{"a":1,"b":null}""", canonical);
    }
}
