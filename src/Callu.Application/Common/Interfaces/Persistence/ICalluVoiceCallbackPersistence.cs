using Callu.Shared.Models.Communication;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>What one callu-voice callback did when it was applied.</summary>
public enum CalluVoiceCallbackApplication
{
    /// <summary>The call log, the incident and the retry chain were updated from this callback.</summary>
    Applied,

    /// <summary>The same call already carried this status, so the delivery was a retry and nothing was replayed.</summary>
    AlreadyApplied,

    /// <summary>A progress callback that arrived after its call had already ended; applying it would undo the outcome.</summary>
    OutOfOrder,

    /// <summary>The token resolved, but the incident it names is gone.</summary>
    IncidentNotFound
}

/// <summary>Applies one callu-voice status callback: call log, incident transition, retry chain and audit.</summary>
public interface ICalluVoiceCallbackPersistence
{
    Task<CalluVoiceCallbackApplication> ProcessAsync(
        CalluVoiceCallbackTicket ticket,
        CalluVoiceCallbackRequest callback,
        CancellationToken cancellationToken = default);
}
