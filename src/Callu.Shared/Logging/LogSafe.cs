namespace Callu.Shared.Logging;

/// <summary>Flattens untrusted text so a value from outside cannot forge a second log line.</summary>
public static class LogSafe
{
    private const int MaxLength = 200;
    private const string Missing = "(none)";

    /// <summary>The value on one line, control characters replaced and length capped.</summary>
    public static string OneLine(string? value)
    {
        // Named rather than blank: a log line that just stops reads as a formatting slip, not as
        // "the sender left this out".
        if (string.IsNullOrEmpty(value)) return Missing;

        var clipped = value.Length > MaxLength ? value[..MaxLength] + "…" : value;
        return string.Create(clipped.Length, clipped, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) ? '·' : c;
            }
        });
    }
}
