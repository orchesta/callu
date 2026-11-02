using Callu.Shared.Exceptions;

namespace Callu.Shared.Models.Incidents;

/// <summary>Why an incident was resolved, closed or reopened, and how it was resolved.</summary>
// Optional in the API so existing clients keep working; the panel asks for them. A transition without
// them is recorded without them: callu does not invent a reason to satisfy a profile.
public sealed record IncidentTransitionNote
{
    public const int MaxReasonLength = 500;

    /// <summary>What the alert source reports when it clears an alert; never typed by a person.</summary>
    public const string AlertCleared = "alert-cleared";

    public static readonly IReadOnlyList<string> ResolutionTypes =
        ["workaround", "permanent-fix", "duplicate", "false-positive", "no-fault"];

    public static readonly IncidentTransitionNote None = new();

    public static readonly IncidentTransitionNote AlertClearedBySource = new()
    {
        ResolutionType = AlertCleared,
        ReasonCode = AlertCleared,
    };

    public string? ResolutionType { get; private init; }
    public string? Reason { get; private init; }
    public string? ReasonCode { get; private init; }

    public static IncidentTransitionNote FromInput(string? resolutionType, string? reason)
    {
        var errors = new Dictionary<string, string[]>();

        var type = string.IsNullOrWhiteSpace(resolutionType) ? null : resolutionType.Trim();
        if (type is not null && !ResolutionTypes.Contains(type))
            errors["resolutionType"] = [$"Must be one of: {string.Join(", ", ResolutionTypes)}."];

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is { Length: > MaxReasonLength })
            errors["reason"] = [$"Must be at most {MaxReasonLength} characters."];

        if (errors.Count > 0) throw new ValidationException(errors);

        return new IncidentTransitionNote { ResolutionType = type, Reason = text };
    }
}
