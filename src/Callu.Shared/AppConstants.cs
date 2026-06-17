namespace Callu.Shared;

/// <summary>
/// Application-wide constants to eliminate magic numbers.
/// NOTE: For enum values (Roles, Severity, Status, etc.), use Callu.Domain.Enums instead.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Pagination defaults
    /// </summary>
    public static class Pagination
    {
        public const int DefaultPageSize = 10;
        public const int MaxPageSize = 100;
        public static readonly int[] PageSizeOptions = [10, 25, 50, 100];
    }

    /// <summary>
    /// Team member roles (not the same as UserRole enum - these are team-level roles)
    /// </summary>
    public static class TeamMemberRole
    {
        public const string Lead = "Lead";
        public const string Member = "Member";
        public const string Observer = "Observer";

        public static readonly string[] All = [Lead, Member, Observer];

        /// <summary>
        /// Case-insensitive role validation. The frontend role-picker emits "lead"/"member"/
        /// "observer" (lowercase, matches its badge style switch), but the DB stores the
        /// canonical "Lead"/"Member"/"Observer". Without case folding, the legitimate
        /// frontend payload is rejected and the AddMember/UpdateRole flow silently fails.
        /// </summary>
        public static bool IsValid(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && Array.Exists(All, r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Returns the canonical capitalized form for storage. Falls back to <see cref="Member"/>
        /// for unknown input — callers should pre-validate with <see cref="IsValid"/>.
        /// </summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Member;
            foreach (var canonical in All)
            {
                if (string.Equals(canonical, value, StringComparison.OrdinalIgnoreCase))
                    return canonical;
            }
            return Member;
        }
    }

    /// <summary>
    /// Team color options for UI. Storage format is a 6-character hex code ("#RRGGBB"),
    /// chosen so the same value renders identically in the SPA, Slack/Discord embeds, and
    /// status-page exports without UI-framework coupling.
    /// </summary>
    public static class TeamColors
    {
        public const string Default = "#3B82F6";

        public static readonly Dictionary<string, string> All = new()
        {
            ["#3B82F6"] = "Blue",
            ["#10B981"] = "Green",
            ["#8B5CF6"] = "Purple",
            ["#F59E0B"] = "Orange",
            ["#EF4444"] = "Red",
            ["#6B7280"] = "Gray"
        };

        /// <summary>
        /// Translate the legacy Tailwind class names (used until 2026-05) to their hex
        /// equivalents so older request payloads still resolve to a valid colour.
        /// </summary>
        public static readonly Dictionary<string, string> LegacyMap = new()
        {
            ["bg-brand-500"] = "#3B82F6",
            ["bg-success-500"] = "#10B981",
            ["bg-purple-500"] = "#8B5CF6",
            ["bg-warning-500"] = "#F59E0B",
            ["bg-error-500"] = "#EF4444",
            ["bg-gray-500"] = "#6B7280"
        };
    }
}
