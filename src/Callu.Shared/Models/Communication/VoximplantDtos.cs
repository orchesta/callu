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

public record VoxCallHistoryQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public int Count { get; init; } = 50;
    public string? RemoteNumber { get; init; }
    public long? CallSessionHistoryId { get; init; }
}

public record VoxCallHistoryDto
{
    public int TotalCount { get; init; }
    public string Timezone { get; init; } = "Etc/GMT";
    public List<VoxCallSessionDto> Sessions { get; init; } = [];
}

public record VoxCallSessionDto
{
    public long CallSessionHistoryId { get; init; }
    public long ApplicationId { get; init; }
    public string? ApplicationName { get; init; }
    public string? StartDate { get; init; }
    public int? DurationSeconds { get; init; }
    public string? FinishReason { get; init; }
    public string? RuleName { get; init; }
    public string? CustomData { get; init; }
    public bool HasLog { get; init; }
    public List<VoxCallLegDto> Calls { get; init; } = [];
}

public record VoxCallLegDto
{
    public long CallId { get; init; }
    public string? LocalNumber { get; init; }
    public string? RemoteNumber { get; init; }
    public string? StartTime { get; init; }
    public int? DurationSeconds { get; init; }
    public decimal? Cost { get; init; }
    public bool? Successful { get; init; }
}

public record VoxSessionLogDto
{
    public long CallSessionHistoryId { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool Truncated { get; init; }
}
