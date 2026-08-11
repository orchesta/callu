using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Callu.Shared.Results;
using Callu.Shared.Models.Conference;
using Microsoft.AspNetCore.Authorization;
using Callu.Shared.Logging;

namespace Callu.Api.Controllers;

/// <summary>Callback endpoints hit by VoxEngine scripts: <c>X-Scenario-Key</c> plus replay protection identifies the
/// scenario, and any state-changing call must also carry the per-call, incident-bound <c>X-Call-Token</c>.</summary>
[ApiController]
[Route("api/voximplant")]
[EnableRateLimiting("callback")]
[Callu.Api.Filters.SkipApiResponseWrapper]
[AllowAnonymous]
public class VoximplantCallbackController(
    ICallDataService callDataService,
    IVideoConferenceService conferenceService,
    IVoximplantReplayGuard replayGuard,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    ILogger<VoximplantCallbackController> logger)
    : ControllerBase
{
    private readonly VoxCallbackTokenProtector _callbackTokens = new(dataProtectionProvider);

    /// <summary>Conference-room statuses exempt from the call-token binding, because the conference scenario has no
    /// token and these are dropped before any write. Only safe while it matches that drop-list exactly.</summary>
    public static readonly IReadOnlySet<string> InertConferenceLifecycleStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "conference_started",
            "conference_ended",
            "participant_joined",
            "participant_left",
        };

    /// <summary>Escape hatch for a deployment whose Voximplant scenarios do not yet send <c>X-Call-Token</c>;
    /// off by default, since accepting unbound callbacks lets a leaked scenario key silence any incident.</summary>
    private bool AllowLegacyUnboundCallbacks =>
        configuration.GetValue("Voximplant:AllowLegacyUnboundCallbacks", false);

    private IActionResult? RejectIfReplayed(string? timestamp, string? nonce)
    {
        if (string.IsNullOrWhiteSpace(timestamp) || !long.TryParse(timestamp, out var ts))
        {
            logger.LogWarning("VoxEngine request rejected: missing/invalid X-Timestamp header");
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > replayGuard.WindowSeconds)
        {
            logger.LogWarning("VoxEngine request rejected: timestamp drift {Delta}s exceeds window", now - ts);
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (string.IsNullOrWhiteSpace(nonce))
        {
            logger.LogWarning("VoxEngine request rejected: missing X-Nonce header");
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (!replayGuard.TryRegister(ts, nonce))
        {
            logger.LogWarning("VoxEngine request rejected: replayed nonce {Nonce}", LogSafe.OneLine(nonce));
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        return null;
    }

    /// <summary>VoxEngine fetches call data via a one-time token.</summary>
    [HttpGet("call-data/{token}")]
    public async Task<IActionResult> GetCallData(
        string token,
        [FromHeader(Name = "X-Scenario-Key")] string? scenarioKey,
        [FromHeader(Name = "X-Timestamp")] string? timestamp,
        [FromHeader(Name = "X-Nonce")] string? nonce)
    {
        logger.LogInformation("call-data request: keyFp={KeyFp}, token={Token}",
            ScenarioKeyFingerprint(scenarioKey ?? string.Empty),
            LogSafe.OneLine(token[..Math.Min(8, token.Length)] + "..."));

        if (string.IsNullOrEmpty(scenarioKey))
        {
            logger.LogWarning("Missing scenario API key for call-data request");
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (RejectIfReplayed(timestamp, nonce) is { } rejected) return rejected;

        var outcome = await callDataService.ConsumeCallTokenWithScenarioCheckAsync(token, scenarioKey);
        switch (outcome.Status)
        {
            case CallTokenConsumeStatus.AlreadyConsumed:
                logger.LogWarning("Call token replay attempt: {Token}", LogSafe.OneLine(token[..Math.Min(8, token.Length)] + "..."));
                return StatusCode(StatusCodes.Status410Gone,
                    ApiResponse.Fail(Messages.Get("voximplant.tokenInvalid")));
            case CallTokenConsumeStatus.Expired:
            case CallTokenConsumeStatus.NotFound:
                logger.LogWarning("Call token invalid or expired: {Token}", LogSafe.OneLine(token[..Math.Min(8, token.Length)] + "..."));
                return NotFound(ApiResponse.Fail(Messages.Get("voximplant.tokenInvalid")));
            case CallTokenConsumeStatus.ScenarioKeyRejected:
                return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        var callData = outcome.Data!;
        logger.LogInformation(
            "Voximplant.CallDataAccess: token={TokenPrefix} incident={IncidentId} keyFp={KeyFp}",
            LogSafe.OneLine(token[..Math.Min(8, token.Length)] + "..."),
            callData.IncidentId,
            ScenarioKeyFingerprint(scenarioKey));

        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Ok(new
        {
            incident_id = callData.IncidentId,
            attempt_id = callData.AttemptId,
            title = callData.Title,
            severity = callData.Severity,
            service_name = callData.ServiceName,
            description = callData.Description,
            phone = callData.Phone,
            country_code = callData.CountryCode,
            sip_server = callData.SipServer,
            sip_username = callData.SipUsername,
            sip_password = callData.SipPassword,
            caller_id = callData.CallerId,

            language = callData.Language,
            tts_messages = callData.TtsMessages,
            conference_id = callData.ConferenceId,
            max_participants = callData.MaxParticipants,
            record = callData.Record,

            // Bearer for this call's status callbacks. Unlike the call-data token it is not
            // single-use — the scenario reports several times over the life of one call.
            callback_token = _callbackTokens.Issue(callData.IncidentId)
        });
    }

    private static string ScenarioKeyFingerprint(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(none)";
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 4);
    }

    private void LogUnboundRejection(string what, string? status)
    {
        logger.LogError(
            "Voximplant {What} '{Status}' rejected: no valid per-call token (X-Call-Token). The " +
            "Voximplant scenario is running a pre-1.4 script — re-provision it (Settings → " +
            "Communications → Provision). To restore the previous, unbound behaviour while you " +
            "schedule that, set Voximplant:AllowLegacyUnboundCallbacks=true; note this lets a " +
            "leaked scenario key acknowledge any incident.",
            what, LogSafe.OneLine(status));
    }

    /// <summary>Status update from VoxEngine (acknowledged, escalated, failed, etc.).</summary>
    [HttpPost("callback")]
    public async Task<IActionResult> ReceiveCallback(
        [FromBody] VoxCallbackRequest callback,
        [FromHeader(Name = "X-Scenario-Key")] string? scenarioKey,
        [FromHeader(Name = "X-Call-Token")] string? callbackToken,
        [FromHeader(Name = "X-Timestamp")] string? timestamp,
        [FromHeader(Name = "X-Nonce")] string? nonce)
    {
        if (string.IsNullOrEmpty(scenarioKey) ||
            !await callDataService.ValidateScenarioApiKeyAsync(scenarioKey))
        {
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (RejectIfReplayed(timestamp, nonce) is { } rejected) return rejected;

        if (_callbackTokens.TryResolveIncident(callbackToken, out var boundIncidentId))
        {
            if (!string.IsNullOrEmpty(callback.IncidentId) &&
                !string.Equals(callback.IncidentId, boundIncidentId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Voximplant callback rejected: body claims incident {Claimed} but the call token is bound to {Bound}",
                    LogSafe.OneLine(callback.IncidentId), LogSafe.OneLine(boundIncidentId));
                return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
            }

            callback.IncidentId = boundIncidentId;
        }
        else if (!InertConferenceLifecycleStatuses.Contains(callback.Status ?? string.Empty))
        {
            if (!AllowLegacyUnboundCallbacks)
            {
                LogUnboundRejection("callback", callback.Status);
                return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
            }

            logger.LogWarning(
                "Voximplant callback '{Status}' accepted WITHOUT a per-call token for incident {IncidentId} " +
                "(Voximplant:AllowLegacyUnboundCallbacks is on). Re-provision the scenarios and turn it off.",
                LogSafe.OneLine(callback.Status), LogSafe.OneLine(callback.IncidentId));
        }

        try
        {
            var result = await callDataService.ProcessCallbackAsync(callback, scenarioKey);

            if (result.EscalationPagedNobody)
                logger.LogWarning(
                    "Responder-initiated escalation for incident {IncidentId} paged nobody; telling the scenario to play the "
                    + "honest prompt (the incident is still open — escalate it in Callu).",
                    LogSafe.OneLine(callback.IncidentId));

            // success says the callback was APPLIED. escalation_paged says what a press-2 ACHIEVED, and
            // it is the only thing the scenario may confirm out loud: a 2xx has always been true for an
            // escalation that paged nobody, and the responder heard "escalation has been initiated"
            // anyway. Non-escalation callbacks carry false and the scenario ignores the field.
            return Ok(new
            {
                success = true,
                escalation_paged = result.EscalationPagedSomeone
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing VoxEngine callback");
            return StatusCode(500, new { error = Messages.Get("voximplant.callbackFailed") });
        }
    }

    /// <summary>VoxEngine asks for a conference room when the responder presses 9.</summary>
    [HttpPost("conference-room")]
    public async Task<IActionResult> CreateConferenceRoom(
        [FromBody] CreateConferenceRoomRequest request,
        [FromHeader(Name = "X-Scenario-Key")] string? scenarioKey,
        [FromHeader(Name = "X-Call-Token")] string? callbackToken,
        [FromHeader(Name = "X-Timestamp")] string? timestamp,
        [FromHeader(Name = "X-Nonce")] string? nonce,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scenarioKey) ||
            !await callDataService.ValidateScenarioApiKeyAsync(scenarioKey, ct))
        {
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (RejectIfReplayed(timestamp, nonce) is { } rejected) return rejected;

        // Only the incident scenario reaches this endpoint (9 during a live call), and it
        // always holds a call token — so there is no inert-status exemption here.
        Guid incidentId;
        if (_callbackTokens.TryResolveIncident(callbackToken, out var boundIncidentId)
            && Guid.TryParse(boundIncidentId, out var boundGuid))
        {
            if (request.IncidentId != Guid.Empty && request.IncidentId != boundGuid)
            {
                logger.LogWarning(
                    "Conference-room request rejected: body claims incident {Claimed} but the call token is bound to {Bound}",
                    request.IncidentId, boundGuid);
                return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
            }

            incidentId = boundGuid;
        }
        else if (AllowLegacyUnboundCallbacks)
        {
            incidentId = request.IncidentId;
            logger.LogWarning(
                "Conference-room request accepted WITHOUT a per-call token for incident {IncidentId} " +
                "(Voximplant:AllowLegacyUnboundCallbacks is on). Re-provision the scenarios and turn it off.",
                incidentId);
        }
        else
        {
            LogUnboundRejection("conference-room request", "conference_room");
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        try
        {
            var result = await conferenceService.CreateRoomAsync(incidentId, ct);

            if (!result.Success)
                return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));

            return Ok(new
            {
                success = true,
                room_id = result.RoomId,
                conference_url = result.ConferenceUrl,
                participant_count = result.ParticipantCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating conference room for incident {IncidentId}", incidentId);
            return StatusCode(500, new { error = Messages.Get("voximplant.conferenceRoomFailed") });
        }
    }
}

/// <summary>Mints and verifies the per-call token binding a VoxEngine status callback to the incident the call was
/// placed for — stateless, the incident id sealed with DataProtection and an absolute lifetime.</summary>
public sealed class VoxCallbackTokenProtector
{
    public const string Purpose = "Callu.Voximplant.CallbackToken.v1";

    /// <summary>Ring + the scenario's own 2-minute cap on the answered leg, with room to spare.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector;

    public VoxCallbackTokenProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>Null for a call with no incident (e.g. a provider test call) — nothing to bind.</summary>
    public string? Issue(string? incidentId) =>
        string.IsNullOrWhiteSpace(incidentId) ? null : _protector.Protect(incidentId, Lifetime);

    /// <summary>
    /// True only for a token this instance sealed, still inside its lifetime. The incident comes
    /// out of the token; callers must not fall back to a request-supplied id when this is false.
    /// </summary>
    public bool TryResolveIncident(string? token, out string incidentId)
    {
        incidentId = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            incidentId = _protector.Unprotect(token.Trim());
            return !string.IsNullOrEmpty(incidentId);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or sealed under a keyring this host cannot read.
            return false;
        }
    }
}
