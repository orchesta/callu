using Callu.Domain.Enums;

namespace Callu.Shared.Models.Communication;

/// <summary>Which provider a single capability is pinned to, if any.</summary>
// Without a route a capability goes to whichever enabled provider declares it and has the best
// priority; a route names one, so voice and video can sit on different providers.
public record CapabilityRouteDto
{
    public CommunicationCapability Capability { get; init; }

    /// <summary>Null when nothing is pinned and the ordinary priority order applies.</summary>
    public Guid? ProviderId { get; init; }

    public string? ProviderName { get; init; }
    public string? ProviderType { get; init; }

    /// <summary>False when the route names a provider that is switched off, so the capability has nowhere to go.</summary>
    public bool IsProviderEnabled { get; init; }

    /// <summary>Every provider that declares this capability, so a caller can offer the real choices.</summary>
    public IReadOnlyList<CapabilityRouteCandidateDto> Candidates { get; init; } = [];
}

public record CapabilityRouteCandidateDto
{
    public Guid ProviderId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
}

/// <summary>Pins one capability to one provider. A null provider clears the pin.</summary>
public record SetCapabilityRouteRequest
{
    public Guid? ProviderId { get; init; }
}
