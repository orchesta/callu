using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Callu.Shared.Audit;

/// <summary>RFC 8785 (JSON Canonicalization Scheme) serialization of a parsed JSON document.</summary>
// An external verifier re-derives the digest from the document it received, so our bytes have to
// match theirs exactly: members sorted by UTF-16 code unit, ECMAScript number formatting, and only
// the escapes the spec allows.
public static class JsonCanonicalizer
{
    public static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        Write(document.RootElement, builder);
        return builder.ToString();
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, builder);
                break;
            case JsonValueKind.Array:
                WriteArray(element, builder);
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, builder);
                break;
            case JsonValueKind.Number:
                builder.Append(FormatNumber(element.GetDouble()));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new InvalidOperationException($"Cannot canonicalize {element.ValueKind}.");
        }
    }

    private static void WriteObject(JsonElement element, StringBuilder builder)
    {
        // Ordinal on the UTF-16 units is what the spec asks for, and it is what StringComparer.Ordinal
        // gives; a culture-aware comparison would order non-ASCII keys differently from every other
        // implementation.
        var members = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        builder.Append('{');
        for (var i = 0; i < members.Length; i++)
        {
            if (i > 0) builder.Append(',');
            WriteString(members[i].Name, builder);
            builder.Append(':');
            Write(members[i].Value, builder);
        }
        builder.Append('}');
    }

    private static void WriteArray(JsonElement element, StringBuilder builder)
    {
        builder.Append('[');
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first) builder.Append(',');
            first = false;
            Write(item, builder);
        }
        builder.Append(']');
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    else
                        builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    /// <summary>ECMAScript Number::toString, which is what RFC 8785 defers to.</summary>
    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidOperationException("NaN and Infinity have no JSON representation.");

        // "-0" round-trips as a distinct double but serializes as "0"; the spec has no negative zero.
        if (value == 0) return "0";

        var sign = value < 0 ? "-" : string.Empty;
        var (digits, pointPosition) = ShortestDigits(Math.Abs(value));
        var k = digits.Length;
        var n = pointPosition;

        // The four cases of ECMAScript Number::toString, in its own order.
        if (k <= n && n <= 21)
            return sign + digits + new string('0', n - k);
        if (0 < n && n <= 21)
            return sign + digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0)
            return sign + "0." + new string('0', -n) + digits;

        var exponent = n - 1;
        var mantissa = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return $"{sign}{mantissa}e{(exponent >= 0 ? "+" : "-")}{Math.Abs(exponent)}";
    }

    /// <summary>Shortest round-trip digits, and where the decimal point sits relative to them.</summary>
    private static (string Digits, int PointPosition) ShortestDigits(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);

        var exponent = 0;
        var eIndex = text.IndexOf('E');
        if (eIndex >= 0)
        {
            exponent = int.Parse(text[(eIndex + 1)..], CultureInfo.InvariantCulture);
            text = text[..eIndex];
        }

        var dotIndex = text.IndexOf('.');
        var integerLength = dotIndex < 0 ? text.Length : dotIndex;
        var allDigits = dotIndex < 0 ? text : text.Remove(dotIndex, 1);

        var leadingZeros = allDigits.Length - allDigits.TrimStart('0').Length;
        var significant = allDigits.Trim('0');
        if (significant.Length == 0) significant = "0";

        return (significant, integerLength - leadingZeros + exponent);
    }
}
