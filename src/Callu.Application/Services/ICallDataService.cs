using Callu.Shared.Models.Communication;

namespace Callu.Application.Services;

/// <summary>Provider-agnostic management of one-time call-data tokens and voice provider callbacks.</summary>
public interface ICallDataService
{
    /// <summary>
    /// Creates a one-time call token and stores the associated call data.
    /// Returns the token that the voice provider can use to fetch data.
    /// </summary>
    Task<string> CreateCallTokenAsync(VoxCallData callData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and invalidates a call token (one-time use).
    /// Returns null if token is invalid, expired, or already used.
    /// </summary>
    Task<VoxCallData?> ConsumeCallTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a call token only if <paramref name="scenarioApiKey"/> matches the configured Voximplant scenario API key.
    /// </summary>
    Task<CallTokenConsumeOutcome> ConsumeCallTokenWithScenarioCheckAsync(
        string token,
        string scenarioApiKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a scenario API key against the stored key for the enabled Voximplant provider.
    /// </summary>
    Task<bool> ValidateScenarioApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Processes a status callback from a voice provider script; when <paramref name="scenarioApiKey"/>
    /// is set it must match the configured Voximplant provider.</summary>
    Task<VoxCallbackResult> ProcessCallbackAsync(
        VoxCallbackRequest callback,
        string? scenarioApiKey = null,
        CancellationToken cancellationToken = default);
}

public enum CallTokenConsumeStatus
{
    NotFound,
    Expired,
    AlreadyConsumed,
    ScenarioKeyRejected,
    Success
}

public readonly record struct CallTokenConsumeOutcome(
    CallTokenConsumeStatus Status,
    VoxCallData? Data);
