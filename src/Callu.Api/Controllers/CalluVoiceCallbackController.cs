using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Shared.Logging;

namespace Callu.Api.Controllers;

/// <summary>The endpoint callu-voice posts call status to; the sealed token in the query is what authenticates it.</summary>
// callu-voice sends no credential of its own and retries the same body, so the URL carries a per-call
// token and a repeat of a status already applied is answered 200 without being applied twice.
// The token sits in the query rather than the path because the query is the part of a request line the
// bundled nginx keeps out of its access log.
[ApiController]
[Route("api/callu-voice")]
[EnableRateLimiting("callback")]
[Callu.Api.Filters.SkipApiResponseWrapper]
[AllowAnonymous]
public class CalluVoiceCallbackController(
    ICalluVoiceCallbackPersistence persistence,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<CalluVoiceCallbackController> logger)
    : ControllerBase
{
    /// <summary>The path this controller answers on, which Callu builds every callback URL from.</summary>
    public const string CallbackPath = CalluVoiceConfig.CallbackPath;

    private const string Rejected = "The callback token is not valid for this call.";

    private readonly CalluVoiceCallbackTokenProtector _tokens = new(dataProtectionProvider);

    [HttpPost("callback")]
    public async Task<IActionResult> ReceiveCallback(
        [FromQuery(Name = CalluVoiceCallbackTokenProtector.TokenQueryKey)] string? token,
        [FromBody] CalluVoiceCallbackRequest callback,
        CancellationToken cancellationToken)
    {
        if (!_tokens.TryResolve(token, out var ticket))
        {
            logger.LogWarning(
                "callu-voice callback '{Status}' rejected: the callback URL carries no token this installation "
                + "sealed, or it has expired. Nothing was acknowledged.",
                LogSafe.OneLine(callback.Status));
            return Unauthorized(new { error = Rejected });
        }

        // The body names the call too, and it is unauthenticated. It may agree with the token or say
        // nothing; it may never redirect the callback at another call.
        if (!string.IsNullOrWhiteSpace(callback.CallId)
            && !string.Equals(callback.CallId.Trim(), ticket.CallId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "callu-voice callback rejected: the body reports call {Claimed}, but the token in the URL was minted "
                + "for a different call on incident {IncidentId}.",
                LogSafe.OneLine(callback.CallId), ticket.IncidentId);
            return Unauthorized(new { error = Rejected });
        }

        try
        {
            var applied = await persistence.ProcessAsync(ticket, callback, cancellationToken);
            return Ok(new { applied = applied == CalluVoiceCallbackApplication.Applied });
        }
        catch (Exception ex)
        {
            // A 500 is retried by callu-voice, which is what a transient database failure needs.
            logger.LogError(ex,
                "Failed to apply the callu-voice callback '{Status}' for incident {IncidentId}",
                LogSafe.OneLine(callback.Status), ticket.IncidentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "The call status could not be recorded." });
        }
    }
}
