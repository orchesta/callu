namespace Callu.Shared.Models.Settings;

/// <summary>
/// Culture information (for formatting, not language)
/// </summary>
public record CultureDto
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string NativeName { get; init; } = string.Empty;
    public string DateFormat { get; init; } = string.Empty;
    public string NumberFormat { get; init; } = string.Empty;
    public string CurrencySymbol { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
}