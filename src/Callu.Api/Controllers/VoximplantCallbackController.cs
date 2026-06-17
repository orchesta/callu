using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Callu.Shared.Results;
using Callu.Shared.Models.Conference;

namespace Callu.Api.Controllers;

/// <summary>
/// Callback endpoints hit by VoxEngine scripts. Auth: X-Scenario-Key header + replay
/// protection via X-Timestamp (5-min window) and X-Nonce (single-use within the window).
/// </summary>
[ApiController]
[Route("api/voximplant")]
[EnableRateLimiting("callback")]
[Callu.Api.Filters.SkipApiResponseWrapper]
public class VoximplantCallbackController(
    ICallDataService callDataService,
    IVideoConferenceService conferenceService,
    IVoximplantReplayGuard replayGuard,
    ILogger<VoximplantCallbackController> logger)
    : ControllerBase
{
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
            logger.LogWarning("VoxEngine request rejected: replayed nonce {Nonce}", nonce);
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
            token[..Math.Min(8, token.Length)] + "...");

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
                logger.LogWarning("Call token replay attempt: {Token}", token[..Math.Min(8, token.Length)] + "...");
                return StatusCode(StatusCodes.Status410Gone,
                    ApiResponse.Fail(Messages.Get("voximplant.tokenInvalid")));
            case CallTokenConsumeStatus.Expired:
            case CallTokenConsumeStatus.NotFound:
                logger.LogWarning("Call token invalid or expired: {Token}", token[..Math.Min(8, token.Length)] + "...");
                return NotFound(ApiResponse.Fail(Messages.Get("voximplant.tokenInvalid")));
            case CallTokenConsumeStatus.ScenarioKeyRejected:
                return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        var callData = outcome.Data!;
        logger.LogInformation(
            "Voximplant.CallDataAccess: token={TokenPrefix} incident={IncidentId} keyFp={KeyFp}",
            token[..Math.Min(8, token.Length)] + "...",
            callData.IncidentId,
            ScenarioKeyFingerprint(scenarioKey));

        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Ok(new
        {
            incident_id = callData.IncidentId,
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
            record = callData.Record
        });
    }

    private static string ScenarioKeyFingerprint(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(none)";
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 4);
    }

    /// <summary>Status update from VoxEngine (acknowledged, escalated, failed, etc.).</summary>
    [HttpPost("callback")]
    public async Task<IActionResult> ReceiveCallback(
        [FromBody] VoxCallbackRequest callback,
        [FromHeader(Name = "X-Scenario-Key")] string? scenarioKey,
        [FromHeader(Name = "X-Timestamp")] string? timestamp,
        [FromHeader(Name = "X-Nonce")] string? nonce)
    {
        if (string.IsNullOrEmpty(scenarioKey) ||
            !await callDataService.ValidateScenarioApiKeyAsync(scenarioKey))
        {
            return Unauthorized(new { error = Messages.Get("voximplant.invalidApiKey") });
        }

        if (RejectIfReplayed(timestamp, nonce) is { } rejected) return rejected;

        try
        {
            await callDataService.ProcessCallbackAsync(callback, scenarioKey);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing VoxEngine callback");
            return StatusCode(500, new { error = Messages.Get("voximplant.callbackFailed") });
        }
    }

    /// <summary>VoxEngine asks for a conference room when the responder presses 999.</summary>
    [HttpPost("conference-room")]
    public async Task<IActionResult> CreateConferenceRoom(
        [FromBody] CreateConferenceRoomRequest request,
        [FromHeader(Name = "X-Scenario-Key")] string? scenarioKey,
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

        try
        {
            var result = await conferenceService.CreateRoomAsync(request.IncidentId, ct);

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
            logger.LogError(ex, "Error creating conference room for incident {IncidentId}", request.IncidentId);
            return StatusCode(500, new { error = Messages.Get("voximplant.conferenceRoomFailed") });
        }
    }
}
