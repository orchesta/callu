namespace Callu.Shared.Models.Communication;

/// <summary>
/// SIP trunk DTOs — response, create request, and update request
/// </summary>

public record SipTrunkDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public int Port { get; init; } = 5060;
    public string Username { get; init; } = string.Empty;
    public string? AuthUser { get; init; }
    public string? CallerId { get; init; }
    public string? DisplayName { get; init; }
    public bool UseTls { get; init; }
    public bool UseTcp { get; init; }
    public bool IsEnabled { get; init; }
}

public record CreateSipTrunkRequest
{
    public string Name { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public int Port { get; init; } = 5060;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? AuthUser { get; init; }
    public string? CallerId { get; init; }
    public string? DisplayName { get; init; }
    public bool UseTls { get; init; }
    public bool UseTcp { get; init; }
}

public record UpdateSipTrunkRequest
{
    public string Name { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public int Port { get; init; } = 5060;
    public string Username { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string? AuthUser { get; init; }
    public string? CallerId { get; init; }
    public string? DisplayName { get; init; }
    public bool UseTls { get; init; }
    public bool UseTcp { get; init; }
    public bool IsEnabled { get; init; } = true;
}
