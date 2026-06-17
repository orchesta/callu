namespace Callu.Shared.Extensions;

/// <summary>C# 14 extension members — trim / normalize for form text (channels, labels, …).</summary>
public static class StringFormExtensions
{
    extension(string? value)
    {
        /// <summary>Trimmed text or empty string when null/whitespace.</summary>
        public string NormalizedTrim() =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
