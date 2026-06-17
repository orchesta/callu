using Callu.Shared.Models.Settings;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for timezone and culture information
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Get all available timezones
    /// </summary>
    IEnumerable<TimezoneDto> GetTimezones();
    
    /// <summary>
    /// Get timezone by ID (e.g., "Europe/Istanbul")
    /// </summary>
    TimezoneDto? GetTimezone(string timezoneId);
    
    /// <summary>
    /// Get all available cultures (for date/number formatting, not language)
    /// </summary>
    IEnumerable<CultureDto> GetCultures();
    
    /// <summary>
    /// Get culture by name (e.g., "tr-TR")
    /// </summary>
    CultureDto? GetCulture(string cultureName);
    
    /// <summary>
    /// Convert UTC time to specified timezone
    /// </summary>
    DateTime ConvertToTimezone(DateTime utcTime, string timezoneId);
    
    /// <summary>
    /// Convert local time from specified timezone to UTC
    /// </summary>
    DateTime ConvertToUtc(DateTime localTime, string timezoneId);
    
    /// <summary>
    /// Get current time in specified timezone
    /// </summary>
    DateTime GetCurrentTime(string timezoneId);
    
    /// <summary>
    /// Format date/time according to culture
    /// </summary>
    string FormatDateTime(DateTime dateTime, string cultureName, string? format = null);
    
    /// <summary>
    /// Format number according to culture
    /// </summary>
    string FormatNumber(decimal number, string cultureName, int decimals = 2);
}
