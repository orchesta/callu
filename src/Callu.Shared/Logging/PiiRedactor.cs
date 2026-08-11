namespace Callu.Shared.Logging;

/// <summary>Shortens personal identifiers for logs: enough to tell people apart, not enough to reach them.</summary>
public static class PiiRedactor
{
    private const string Missing = "(none)";

    /// <summary>An address reduced to its first character and its domain.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return Missing;

        var value = email.Trim();
        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1) return Mask(value);

        var local = value[..at];
        var domain = value[(at + 1)..];
        return $"{local[0]}{new string('*', Math.Min(local.Length - 1, 3))}@{domain}";
    }

    /// <summary>A number reduced to its last four digits.</summary>
    public static string Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return Missing;

        var digits = new string([.. phone.Where(char.IsDigit)]);
        return digits.Length <= 4 ? Mask(phone.Trim()) : $"***{digits[^4..]}";
    }

    /// <summary>A "Name (number)" label with the number shortened and the name left readable.</summary>
    public static string Recipient(string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return Missing;

        var value = recipient.Trim();
        var open = value.LastIndexOf('(');
        if (open <= 0 || !value.EndsWith(')')) return Phone(value);

        return $"{value[..open].TrimEnd()} ({Phone(value[(open + 1)..^1])})";
    }

    private static string Mask(string value) => new('*', Math.Min(value.Length, 6));
}
