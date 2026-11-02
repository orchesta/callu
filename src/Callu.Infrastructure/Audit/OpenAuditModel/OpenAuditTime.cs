using System.Globalization;

namespace Callu.Infrastructure.Audit.OpenAuditModel;

/// <summary>A timestamp as callu writes it into an OpenAuditModel document.</summary>
// Microseconds because that is what Postgres gives back: a finer value would describe a moment the
// row can never be read back at. Trailing zeros go so a whole second reads the way people write it.
public static class OpenAuditTime
{
    public static string Format(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };
        var micro = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

        var text = micro.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture)
            .TrimEnd('0')
            .TrimEnd('.');

        return text + "Z";
    }
}
