namespace Callu.Shared.Models.Communication;

/// <summary>
/// Voximplant resource DTOs — Account, Application, User, Scenario, Rule
/// </summary>

public record VoxAccountInfoDto
{
    public long AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string AccountEmail { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Currency { get; init; } = "USD";
    public bool Active { get; init; }
}

public record VoxApplicationDto
{
    public long ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public DateTime? Modified { get; init; }
    public bool SecureRecordStorage { get; init; }
}

public record VoxUserDto
{
    public long UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Active { get; init; }
    public string? CustomData { get; init; }
}

public record VoxScenarioDto
{
    public long ScenarioId { get; init; }
    public string ScenarioName { get; init; } = string.Empty;
    public DateTime? Modified { get; init; }
    public string? ScenarioScript { get; init; }
}

public record VoxRuleDto
{
    public long RuleId { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public string RulePattern { get; init; } = string.Empty;
    public string? RulePatternExclude { get; init; }
    public bool VideoConference { get; init; }
    public DateTime? Modified { get; init; }
    public List<VoxScenarioDto> Scenarios { get; init; } = new();
}
