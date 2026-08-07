namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>Maps a Callu user id to its Voximplant username; provisioning and conference join must
/// derive it identically or join creates a duplicate user per conference.</summary>
internal static class VoximplantUserNaming
{
    public static string Sanitize(string userId)
    {
        var sanitized = userId.ToLowerInvariant()
            .Replace("@", "-at-")
            .Replace(".", "-");

        if (sanitized.Length > 0 && !char.IsLetterOrDigit(sanitized[0]))
            sanitized = "u" + sanitized;

        if (sanitized.Length > 50)
            sanitized = sanitized[..50];

        return sanitized;
    }
}
