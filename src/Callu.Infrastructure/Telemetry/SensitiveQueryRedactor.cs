using System.Text;

namespace Callu.Infrastructure.Telemetry;

public static class SensitiveQueryRedactor
{
    private const string RedactedValue = "REDACTED";

    /// <summary>
    /// Query-string parameter names whose values are replaced before a URL reaches telemetry.
    /// </summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key", "apikey", "key", "token", "access_token", "accesstoken",
        "password", "pwd", "secret", "client_secret", "sig", "signature",
        "user_password", "userpassword", "child_account_api_key", "auth_hash",
        "refresh_token", "refreshtoken", "call_token", "calltoken", "scenario_key",
    };

    internal static bool HasSensitiveQuery(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query)) return false;
        foreach (var pair in EnumeratePairs(uri.Query))
        {
            if (SensitiveKeys.Contains(pair.key)) return true;
        }
        return false;
    }

    internal static string Redact(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
            return uri.GetLeftPart(UriPartial.Path);

        return $"{uri.GetLeftPart(UriPartial.Path)}?{RedactQuery(uri.Query)}";
    }

    /// <summary>A raw request target with the value of every sensitive query parameter replaced.</summary>
    public static string RedactTarget(string target)
    {
        var split = target.IndexOf('?');
        if (split < 0 || split == target.Length - 1) return target;

        return string.Concat(target.AsSpan(0, split + 1), RedactQuery(target[(split + 1)..]));
    }

    /// <summary>A query string, without its leading '?', with the value of every sensitive parameter replaced.</summary>
    public static string RedactQuery(string query)
    {
        var sb = new StringBuilder(query.Length);
        var first = true;
        foreach (var pair in EnumeratePairs(query))
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(pair.key);
            if (pair.hasValue)
            {
                sb.Append('=');
                sb.Append(SensitiveKeys.Contains(pair.key) ? RedactedValue : pair.value);
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<(string key, string value, bool hasValue)> EnumeratePairs(string query)
    {
        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0)
                yield return (segment, string.Empty, false);
            else
                yield return (segment[..eq], segment[(eq + 1)..], true);
        }
    }
}
