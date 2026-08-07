using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>Places pages through a self-hosted callu-voice service.</summary>
public class CalluVoiceProvider(
    IHttpClientFactory httpClientFactory,
    ITtsTemplateService ttsTemplateService,
    ProviderSecretProtector secretProtector,
    Voximplant.SipTrunkPasswordProtector sipTrunkProtector,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<CalluVoiceProvider> logger) : BaseCommunicationProvider, IVoicePreviewProvider
{
    private readonly CalluVoiceCallbackTokenProtector _callbackTokens = new(dataProtectionProvider);

    internal const string HttpClientName = "CalluVoice";

    /// <summary>The name this adapter is registered and stored under.</summary>
    public const string TypeName = "callu-voice";

    /// <summary>Default wait for POST /calls, which renders every prompt before it answers.</summary>
    internal const int DefaultTimeoutSeconds = 60;
    internal const int MinTimeoutSeconds = 5;
    internal const int MaxTimeoutSeconds = 90;

    private const int MaxReportedErrorChars = 200;

    private CalluVoiceConfig? _config;

    public override string ProviderType => TypeName;

    public override CommunicationCapability Capabilities => CommunicationCapability.VoiceCalls;

    /// <summary>Binds the config and decrypts the API token, which is stored encrypted at rest.</summary>
    public override async Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk)
    {
        await base.InitializeAsync(configJson, sipTrunk);
        _config = GetConfig<CalluVoiceConfig>();
        if (_config is not null)
            _config.ApiToken = secretProtector.Unprotect(_config.ApiToken);
    }

    /// <summary>Sends the configured carrier to the voice service and asks it to reload.</summary>
    // Called when an operator saves the provider, not on every load: reaching out on every registry
    // reload would put the network on a path that has to succeed for the screen to open.
    public Task<(bool Applied, string? Error)> ApplyTrunkAsync() => PushTrunkAsync(DesiredTrunk());

    /// <summary>Takes the carrier off the voice service, which otherwise keeps registering with it.</summary>
    public Task<(bool Applied, string? Error)> ClearTrunkAsync() => PushTrunkAsync(CalluVoiceTrunk.None);

    /// <summary>The carrier this provider is configured to dial through, with its password decrypted.</summary>
    private CalluVoiceTrunk DesiredTrunk() =>
        CalluVoiceTrunk.From(SipTrunk, SipTrunk is null ? string.Empty : sipTrunkProtector.Unprotect(SipTrunk.Password));

    /// <summary>Sends the carrier again only when the voice service is no longer holding it.</summary>
    // The service keeps no carrier across a restart, and nothing else notices: without this, a
    // container recreate leaves the panel showing a carrier that no longer exists anywhere.
    public async Task<CalluVoiceTrunkReconciliation> ReconcileTrunkAsync()
    {
        var desired = DesiredTrunk();

        var (read, reported, readError) = await ReadTrunkFingerprintAsync();
        if (!read)
            return CalluVoiceTrunkReconciliation.CouldNotRead(readError);

        if (string.Equals(reported, desired.Fingerprint(), StringComparison.Ordinal))
            return CalluVoiceTrunkReconciliation.InAgreement();

        var (applied, pushError) = await PushTrunkAsync(desired);
        return applied
            ? CalluVoiceTrunkReconciliation.Restored()
            : CalluVoiceTrunkReconciliation.CouldNotRestore(pushError);
    }

    /// <summary>What the voice service says it is holding, as a fingerprint it never has to disclose a password for.</summary>
    private async Task<(bool Read, string? Fingerprint, string? Error)> ReadTrunkFingerprintAsync()
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.BaseUrl))
            return (false, null, "Provider not configured (missing base URL).");
        if (!TryBuildUri("trunk", out var uri))
            return (false, null, $"Invalid base URL: {_config.BaseUrl}");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);

            using var cts = new CancellationTokenSource(RequestTimeout);
            using var response = await client.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)response.StatusCode}: {Truncate(body)}");

            // An older service answers 200 without the field. Treating that as "no carrier" would
            // re-push on every sweep forever, so it is a read failure and says so.
            return ReadString(body, "fingerprint") is { Length: > 0 } fingerprint
                ? (true, fingerprint, null)
                : (false, null, "GET /trunk answered without a fingerprint; the voice service is older than this version of Callu.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<(bool Applied, string? Error)> PushTrunkAsync(CalluVoiceTrunk trunk)
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.BaseUrl))
            return (false, "Provider not configured (missing base URL).");
        if (!TryBuildUri("trunk", out var uri))
            return (false, $"Invalid base URL: {_config.BaseUrl}");

        var payload = trunk.ToRequestBody();

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);

            using var cts = new CancellationTokenSource(RequestTimeout);
            using var response = await client.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                logger.LogError(
                    "callu-voice refused the trunk configuration ({Status}): {Body}. "
                    + "Calls will use whatever carrier it already had.",
                    (int)response.StatusCode, Truncate(body));
                return (false, Truncate(body));
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not send the trunk configuration to callu-voice. "
                + "Calls will use whatever carrier it already had.");
            return (false, ex.Message);
        }
    }

    private TimeSpan RequestTimeout => TimeSpan.FromSeconds(
        Math.Clamp(_config?.RequestTimeoutSeconds ?? DefaultTimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds));

    public override async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.BaseUrl))
            return (false, "Provider not configured (missing base URL).");

        if (!TryBuildUri("health", out var healthUri) || !TryBuildUri("calls", out var callsUri))
            return (false, $"Invalid base URL: {_config.BaseUrl}");

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var cts = new CancellationTokenSource(RequestTimeout);

        HttpStatusCode healthStatus;
        string healthBody;
        try
        {
            using var response = await client.GetAsync(healthUri, cts.Token);
            healthStatus = response.StatusCode;
            healthBody = await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return (false, $"callu-voice did not answer within {RequestTimeout.TotalSeconds:0}s at {healthUri}.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Could not reach callu-voice at {healthUri}: {ex.Message}");
        }

        if (!LooksLikeCalluVoice(healthBody))
            return (false, $"{healthUri} answered HTTP {(int)healthStatus}, but not with a callu-voice health "
                           + "document. Check that the base URL points at callu-voice.");

        if (healthStatus != HttpStatusCode.OK)
            return (false, $"callu-voice is reachable but cannot place calls right now (HTTP {(int)healthStatus}): "
                           + Truncate(healthBody));

        // The token is checked before the body is read, so an empty body proves the token without
        // any risk of dialling: callu-voice answers 401 for a bad one and 400 for a call_id it lacks.
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Post, callsUri)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            Authorize(probe);

            using var response = await client.SendAsync(probe, cts.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "callu-voice is reachable, but it rejected the API token.");
            if (response.StatusCode != HttpStatusCode.BadRequest)
                return (false, $"callu-voice answered HTTP {(int)response.StatusCode} to the token check; "
                               + "expected 400 for a request with no call id.");
        }
        catch (OperationCanceledException)
        {
            return (false, $"callu-voice did not answer the token check within {RequestTimeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Could not reach callu-voice at {callsUri}: {ex.Message}");
        }

        return (true, $"Connected to callu-voice at {healthUri.Authority}; the API token was accepted.");
    }

    /// <summary>Refused: callu-voice exposes no way to end a call from outside it.</summary>
    // The base class would return quietly here, because this provider does place voice calls — and a
    // caller told a call was hung up when it is still up is worse off than one told it cannot be.
    public override Task HangupCallAsync(string callId) =>
        throw new NotSupportedException(
            "callu-voice ends its own calls; there is no endpoint to hang one up from Callu.");

    public override async Task<CallResult> MakeCallAsync(MakeCallRequest request)
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.BaseUrl))
            return new CallResult { Success = false, ErrorMessage = "Provider not configured (missing base URL)." };

        // Second half of the create/enable gate: a row edited around the API must not dial either,
        // because a call nothing reports on ends in a spoken confirmation and an incident still open.
        if (CalluVoiceConfig.RefuseCallbackUrl(_config.CallbackUrl) is { } refusal)
        {
            logger.LogError(
                "callu-voice: nothing was dialled for incident {IncidentId}. {Refusal}",
                request.IncidentId, refusal);
            return new CallResult { Success = false, ErrorMessage = refusal };
        }

        if (!TryBuildUri("calls", out var callsUri))
            return new CallResult { Success = false, ErrorMessage = $"Invalid base URL: {_config.BaseUrl}" };

        var destination = CalluVoiceDestination.Normalize(request.Destination);
        if (!destination.IsUsable)
        {
            logger.LogError(
                "callu-voice: nothing was dialled for incident {IncidentId}. {Refusal}",
                request.IncidentId, destination.Refusal);
            return new CallResult { Success = false, ErrorMessage = destination.Refusal };
        }

        var callId = CallIdFor(request);
        var language = string.IsNullOrWhiteSpace(request.Language) ? SupportedCultures.Fallback : request.Language;

        // The resolved language, not the requested one: when resolution falls back to another
        // language's template, the prompts have to be spoken in the language they are written in.
        var resolved = await ttsTemplateService.ResolveMessagesAsync(language);

        var body = CalluVoiceRequestBuilder.Build(
            request, destination.Number, callId, resolved.Messages, _config.Voice,
            CallbackUrlFor(request, callId, destination.Number),
            resolved.LanguageCode);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var cts = new CancellationTokenSource(RequestTimeout);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, callsUri)
            {
                Content = JsonContent.Create(body, options: CalluVoiceJson.Options)
            };
            Authorize(httpRequest);

            using var response = await client.SendAsync(httpRequest, cts.Token);
            var payload = await response.Content.ReadAsStringAsync(cts.Token);

            return Interpret(response.StatusCode, payload, callId, request);
        }
        catch (OperationCanceledException ex)
        {
            // Thrown, not returned false: the render may have finished and the phone may be ringing,
            // so the caller has to treat this as "cannot tell" rather than re-dial straight away.
            throw new TimeoutException(
                $"callu-voice did not answer POST /calls for incident {request.IncidentId} within "
                + $"{RequestTimeout.TotalSeconds:0}s; whether the call was placed is unknown.", ex);
        }
        catch (HttpRequestException ex) when (CalluVoiceDialAnswers.NeverReachedTheService(ex.HttpRequestError))
        {
            logger.LogWarning(ex,
                "callu-voice at {Uri} could not be reached for incident {IncidentId}; no call was placed",
                callsUri, request.IncidentId);
            return new CallResult
            {
                Success = false,
                ErrorMessage = $"callu-voice at {callsUri.Authority} could not be reached, so no call was placed: {ex.Message}"
            };
        }
    }

    private CallResult Interpret(HttpStatusCode status, string payload, string callId, MakeCallRequest request)
    {
        var code = (int)status;

        switch (CalluVoiceDialAnswers.ForStatusCode(code))
        {
            case CalluVoiceDialAnswer.Placed:
                logger.LogInformation(
                    "callu-voice accepted call {CallId} for incident {IncidentId}",
                    callId, request.IncidentId);
                return new CallResult { Success = true, CallId = ReadString(payload, "call_id") ?? callId };

            case CalluVoiceDialAnswer.AlreadyInFlight:
                logger.LogWarning(
                    "callu-voice already has call {CallId} for incident {IncidentId} in flight; that call reports on this page",
                    callId, request.IncidentId);
                return new CallResult { Success = true, CallId = callId };

            case CalluVoiceDialAnswer.Refused:
                var reason = ReadString(payload, "error") ?? Truncate(payload);
                logger.LogWarning(
                    "callu-voice refused the call for incident {IncidentId} with HTTP {Status}: {Reason}",
                    request.IncidentId, code, reason);
                return new CallResult
                {
                    Success = false,
                    ErrorMessage = $"callu-voice refused the call (HTTP {code}): {reason}"
                };

            default:
                throw new HttpRequestException(
                    $"callu-voice answered HTTP {code} to POST /calls for incident {request.IncidentId}; "
                    + "whether the call was placed is unknown.");
        }
    }

    /// <summary>Renders text the way a call does and reports what each segment became, without dialling.</summary>
    public async Task<TtsPreviewResult> PreviewAsync(TtsPreviewRequest request, CancellationToken cancellationToken)
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.BaseUrl))
            return new TtsPreviewResult(false, [], "Provider not configured (missing base URL).");

        if (!TryBuildUri("preview", out var previewUri))
            return new TtsPreviewResult(false, [], $"Invalid base URL: {_config.BaseUrl}");

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, previewUri)
            {
                Content = JsonContent.Create(
                    new { segments = request.Segments, voice = string.IsNullOrWhiteSpace(request.Voice) ? _config.Voice : request.Voice },
                    options: CalluVoiceJson.Options)
            };
            Authorize(httpRequest);

            using var response = await client.SendAsync(httpRequest, cts.Token);
            var payload = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // The service's own sentence where it wrote one: a preview turned away because a call
                // is being rendered is not a refusal of the text, and reads badly as one.
                var reason = ReadString(payload, "error") ?? Truncate(payload);
                return new TtsPreviewResult(false, [],
                    response.StatusCode == HttpStatusCode.ServiceUnavailable
                        ? reason
                        : $"The voice service refused the preview (HTTP {(int)response.StatusCode}): {reason}");
            }

            var body = JsonSerializer.Deserialize<PreviewResponse>(payload, CalluVoiceJson.Options);
            return new TtsPreviewResult(true, body?.Segments ?? [], null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TtsPreviewResult(false, [], $"The voice service did not answer within {RequestTimeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "callu-voice at {Uri} could not be reached for a preview", previewUri);
            return new TtsPreviewResult(false, [], $"callu-voice at {previewUri.Authority} could not be reached: {ex.Message}");
        }
    }

    /// <summary>Sample incident text, chosen for what it exposes rather than for looking tidy.</summary>
    // A number that must not be read as a quantity, a Latin-script service name inside whatever
    // language the template is in, and a description long enough to hear where it gets clipped.
    // Taken in the template's own language: sample data in another one is read by a voice that
    // cannot pronounce it, which is a defect in the preview rather than in the template.
    private const string SampleTitleKey = "providers.previewSampleTitle";
    private const string SampleServiceKey = "providers.previewSampleService";
    private const string SampleDescriptionKey = "providers.previewSampleDescription";

    public async Task<TtsPreviewResult> PreviewTemplateAsync(string languageCode, CancellationToken cancellationToken)
    {
        var language = string.IsNullOrWhiteSpace(languageCode) ? SupportedCultures.Fallback : languageCode;
        var resolved = await ttsTemplateService.ResolveMessagesAsync(language, cancellationToken);

        var call = CalluVoiceRequestBuilder.Build(
            new MakeCallRequest
            {
                Destination = "+900000000000",
                IncidentTitle = Messages.GetIn(resolved.LanguageCode, SampleTitleKey),
                ServiceName = Messages.GetIn(resolved.LanguageCode, SampleServiceKey),
                Description = Messages.GetIn(resolved.LanguageCode, SampleDescriptionKey),
                Severity = "Critical",
                Language = language,
                // Both halves take the resolved language: a preview of one template should sound like
                // that template. Hearing two languages at once is what the free-text form is for.
                DataLanguage = resolved.LanguageCode,
            },
            destination: "+900000000000",
            callId: "preview",
            messages: resolved.Messages,
            voice: _config?.Voice,
            callbackUrl: null,
            promptLanguage: resolved.LanguageCode);

        var segments = call.Announcement.Concat(call.Prompt)
            .Select(s => new TtsPreviewSegmentRequest { Text = s.Text, Lang = s.Lang })
            .ToList();

        return await PreviewAsync(new TtsPreviewRequest { Segments = segments }, cancellationToken);
    }

    /// <summary>Streams one rendered segment as a WAV; null when the voice service has no audio under that key.</summary>
    public async Task<Stream?> PreviewAudioAsync(string key, CancellationToken cancellationToken)
    {
        if (_config is null || !TryBuildUri($"preview/audio/{Uri.EscapeDataString(key)}", out var audioUri))
            return null;

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, audioUri);
        Authorize(request);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private sealed record PreviewResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("segments")] List<TtsPreviewSegment> Segments);

    /// <summary>The id callu-voice dedupes on, taken from the page attempt so that dialling the same attempt twice is refused.</summary>
    // The attempt's own row id: the notification row on a page, the call-log row on a retry chain. Both are
    // primary keys that survive a restart, and both change when the attempt genuinely does.
    internal static string CallIdFor(Guid attemptId) => attemptId.ToString("N");

    private static string CallIdFor(MakeCallRequest request) =>
        CallIdFor(request.AttemptId ?? Guid.NewGuid());

    /// <summary>The callback address for this call, carrying its own sealed token.</summary>
    // Nothing else authenticates the status callu-voice posts back, so a call with no incident to bind a
    // token to gets an address with no token on it and whatever it reports is refused on arrival.
    private string CallbackUrlFor(MakeCallRequest request, string callId, string number) =>
        CalluVoiceCallbackTokenProtector.CallbackUrlFor(
            _config!.CallbackUrl!, _callbackTokens.Issue(request.IncidentId ?? Guid.Empty, callId, number));

    private void Authorize(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config?.ApiToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);
    }

    private bool TryBuildUri(string path, out Uri uri)
    {
        uri = null!;
        var configured = _config?.BaseUrl?.Trim();
        if (string.IsNullOrEmpty(configured)) return false;

        if (!Uri.TryCreate(configured.TrimEnd('/') + "/", UriKind.Absolute, out var root)) return false;
        if (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps) return false;

        return Uri.TryCreate(root, path, out uri!);
    }

    /// <summary>Whether a /health body is callu-voice's own rather than some other service's.</summary>
    private static bool LooksLikeCalluVoice(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("active_calls", out _)
                   && document.RootElement.TryGetProperty("checks", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(string body, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxReportedErrorChars ? trimmed : trimmed[..MaxReportedErrorChars] + "…";
    }
}
