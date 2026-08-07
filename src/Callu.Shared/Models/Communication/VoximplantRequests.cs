namespace Callu.Shared.Models.Communication;

/// <summary>
/// Voximplant Management API request DTOs — Application, User, Scenario, Rule
/// </summary>

public record CreateVoxApplicationRequest
{
    public string ApplicationName { get; init; } = string.Empty;
}

public record CreateVoxUserRequest
{
    public long ApplicationId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public record CreateVoxScenarioRequest
{
    public long? ApplicationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Script { get; init; } = string.Empty;
    public bool Rewrite { get; init; }
    public long? RuleId { get; init; }
}

public record UpdateVoxScenarioRequest
{
    public long ScenarioId { get; init; }
    public string? Name { get; init; }
    public string? Script { get; init; }
}

public record CreateVoxRuleRequest
{
    public long ApplicationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Pattern { get; init; } = ".*";
    public long[] ScenarioIds { get; init; } = Array.Empty<long>();
    public bool VideoConference { get; init; }
}
