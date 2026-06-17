using Callu.Shared.Models.Communication;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>Callbacks supplied by <see cref="Services.ICallDataService"/> to avoid circular DI.
/// Retry scheduling was removed from this surface — it is now handled durably by
/// <c>VoiceCallRetryQuartzJob</c>, which polls <c>CallLog.NextRetryAt</c>.</summary>
public sealed record VoximplantCallbackProcessingCallbacks(
    Func<string, CancellationToken, Task<VoxCallData?>> PeekCallTokenAsync,
    Func<string, CancellationToken, Task<bool>> ValidateScenarioKeyAsync,
    Func<CancellationToken, Task> NotifyActiveVoiceCallsChanged);

/// <summary>
/// Applies a single voice callback transaction (incident, call log, timeline, escalation side effects).
/// </summary>
public interface IVoximplantVoiceCallbackPersistence
{
    Task ProcessAsync(
        VoxCallbackRequest callback,
        string? scenarioApiKey,
        VoximplantCallbackProcessingCallbacks callbacks,
        CancellationToken cancellationToken = default);
}
