using System.Net;
using System.Text.RegularExpressions;

namespace Callu.Infrastructure.Email;

/// <summary>Shared placeholder substitution for email templates; values are HTML-escaped unless the
/// variable name ends in <c>_raw</c>.</summary>
internal static partial class TemplateVariableRenderer
{
    internal static string ReplaceVariables(string content, IReadOnlyDictionary<string, string> variables) =>
        ReplaceCore(content, variables, htmlEncode: true);

    /// <summary>Plain-text substitution (no HTML encoding) for email subject lines.</summary>
    internal static string ReplaceVariablesPlain(string content, IReadOnlyDictionary<string, string> variables) =>
        ReplaceCore(content, variables, htmlEncode: false);

    private static string ReplaceCore(string content, IReadOnlyDictionary<string, string> variables, bool htmlEncode)
    {
        // Single pass over the template's own placeholders: sequential string.Replace
        // could re-scan substituted VALUES, letting a value containing "{{OtherVar}}"
        // get expanded by a later iteration (latent injection).
        return VariablePattern().Replace(content, m =>
        {
            var key = m.Groups[1].Value;
            if (!variables.TryGetValue(key, out var value))
                return m.Value; // unknown token stays intact
            if (!htmlEncode || key.EndsWith("_raw", StringComparison.Ordinal))
                return value ?? string.Empty;
            return WebUtility.HtmlEncode(value ?? string.Empty);
        });
    }

    /// <summary>Extract all distinct {{variable}} names from template content.</summary>
    internal static List<string> ExtractVariables(string content)
    {
        var matches = VariablePattern().Matches(content);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex VariablePattern();
}
