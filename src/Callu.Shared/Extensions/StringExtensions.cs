namespace Callu.Shared.Extensions;

/// <summary>
/// Extension methods for strings
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Get initials from a name (e.g., "John Doe" → "JD")
    /// </summary>
    public static string GetInitials(this string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        
        return name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
    }
    
    /// <summary>
    /// Truncate string to max length with ellipsis
    /// </summary>
    public static string Truncate(this string? value, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= maxLength) return value;
        
        return value[..(maxLength - suffix.Length)] + suffix;
    }

    /// <summary>
    /// Get display name from first/last name, fallback to email
    /// </summary>
    public static string FormatDisplayName(string? firstName, string? lastName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            var fullName = $"{firstName} {lastName}".Trim();
            return fullName;
        }
        
        return email ?? "Unknown";
    }
}
