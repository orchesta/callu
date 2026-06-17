using Callu.Domain.Enums;

namespace Callu.Shared.Models.Communication;

/// <summary>
/// Communication provider DTOs — response, create request, and update request
/// </summary>

public record CommunicationProviderDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public CommunicationCapability Capabilities { get; init; }
    public Guid? SipTrunkId { get; init; }
    public string? SipTrunkName { get; init; }
    public bool IsEnabled { get; init; }
    public int Priority { get; init; }
    public DateTime? LastTestedAt { get; init; }
    public string? LastTestResult { get; init; }

    public string? VoximplantAccountId { get; init; }
    public string? VoximplantApiKey { get; init; }
    public string? VoximplantNode { get; init; }

    public long? VoximplantApplicationId { get; init; }
    public string? VoximplantApplicationName { get; init; }
    public long? VoximplantScenarioId { get; init; }
    public string? VoximplantScenarioName { get; init; }
    public long? VoximplantRuleId { get; init; }
    public string? VoximplantRuleName { get; init; }

    public string? VerimorUsername { get; init; }
    public string? VerimorPassword { get; init; }
    public string? VerimorSenderId { get; init; }

    public HttpSmsConfigDto? HttpSms { get; init; }
}

/// <summary>
/// Non-secret view of a generic HTTP SMS provider's config, returned to the UI for editing.
/// Secret values (apiKey/username/password) are never echoed — only Has* flags indicate that
/// one is stored, so the form can show "configured" without leaking the value.
/// </summary>
public record HttpSmsConfigDto
{
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = "POST";
    public string ContentType { get; init; } = "json";
    public string? SenderId { get; init; }
    public string? BodyTemplate { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? SuccessMode { get; init; }
    public string? SuccessField { get; init; }
    public string? SuccessValue { get; init; }
    public string? MessageIdPath { get; init; }
    public bool HasApiKey { get; init; }
    public bool HasUsername { get; init; }
    public bool HasPassword { get; init; }
}

/// <summary>Send a one-off test SMS through a provider to verify configuration end-to-end.</summary>
public record TestSmsRequest
{
    public string To { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public record CreateProviderRequest
{
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public Dictionary<string, object> Config { get; init; } = new();
    public Guid? SipTrunkId { get; init; }
    public int Priority { get; init; } = 0;
}

public record UpdateProviderRequest
{
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, object>? Config { get; init; }
    public Guid? SipTrunkId { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
}
