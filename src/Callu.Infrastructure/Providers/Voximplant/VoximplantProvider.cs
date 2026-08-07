using System.Net.Http.Json;
using System.Text.Json;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers.Voximplant.Models;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging;

using Callu.Shared.Localization;

namespace Callu.Infrastructure.Providers.Voximplant;

public class VoximplantProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<VoximplantProvider> logger,
    ICallDataService callDataService,
    ITtsTemplateService ttsTemplateService,
    ProviderSecretProtector secretProtector,
    SipTrunkPasswordProtector sipTrunkProtector)
    : BaseCommunicationProvider, IDisposable
{
    private VoximplantConfig? _config;
    private VoximplantJwtAuthenticator? _jwt;

    private const string VoximplantApiBase = "https://api.voximplant.com/platform_api";
    
    public override string ProviderType => "voximplant";
    
    public override CommunicationCapability Capabilities => 
        CommunicationCapability.VoiceCalls |
        CommunicationCapability.VideoConference |
        CommunicationCapability.TTS |
        CommunicationCapability.ASR |
        CommunicationCapability.Recording |
        CommunicationCapability.VoicemailDetection;

    public override async Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk)
    {
        await base.InitializeAsync(configJson, sipTrunk);
        _config = GetConfig<VoximplantConfig>();
        if (_config is not null)
        {
            _config.ApiKey = secretProtector.Unprotect(_config.ApiKey);
            if (!string.IsNullOrEmpty(_config.ServiceAccountJson))
                _config.ServiceAccountJson = secretProtector.Unprotect(_config.ServiceAccountJson);
        }
        _jwt?.Dispose();
        _jwt = TryBuildJwt(_config);
    }

    private VoximplantJwtAuthenticator? TryBuildJwt(VoximplantConfig? config)
    {
        if (string.IsNullOrWhiteSpace(config?.ServiceAccountJson)) return null;
        try
        {
            return new VoximplantJwtAuthenticator(config.ServiceAccountJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voximplant service account JSON present but could not initialize JWT authenticator — falling back to api_key.");
            return null;
        }
    }

    public VoximplantManagementClient CreateManagementClient(ILogger clientLogger)
    {
        if (_config == null)
            throw new InvalidOperationException("Provider not configured");

        var httpClient = httpClientFactory.CreateClient("Voximplant");
        return new VoximplantManagementClient(httpClient, _config.AccountId, _config.ApiKey, clientLogger, _jwt);
    }

    public VoximplantConfig? GetCurrentConfig() => _config;

    /// <summary>Provisioning application_id — where users are created. Separate from AccountId.</summary>
    public long? GetProvisioningApplicationId()
    {
        if (string.IsNullOrEmpty(ConfigJson)) return null;
        var fullConfig = System.Text.Json.JsonSerializer.Deserialize<VoximplantConfigWithProvisioning>(
            ConfigJson, Models.VoximplantJsonOptions.Read);
        return fullConfig?.Provisioning?.ApplicationId is > 0 ? fullConfig.Provisioning.ApplicationId : null;
    }
    
    public override async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (_config == null)
            return (false, "Provider not configured");

        try
        {
            var client = httpClientFactory.CreateClient("Voximplant");
            var url = BuildPlatformUrl("GetAccountInfo", null);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyManagementAuth(request);

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<VoximplantApiResponse>();
                if (result?.Error == null)
                {
                    return (true, $"Connected to Voximplant account: {_config.AccountId}");
                }
                return (false, $"Voximplant API error: {result.Error.Msg}");
            }

            return (false, $"HTTP error: {response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to test Voximplant connection");
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private string BuildPlatformUrl(string method, IDictionary<string, string>? extra)
    {
        var url = _jwt is not null
            ? $"{VoximplantApiBase}/{method}?account_id={_config!.AccountId}"
            : $"{VoximplantApiBase}/{method}?account_id={_config!.AccountId}&api_key={Uri.EscapeDataString(_config.ApiKey)}";
        if (extra is null) return url;
        foreach (var kvp in extra)
        {
            url += $"&{kvp.Key}={Uri.EscapeDataString(kvp.Value)}";
        }
        return url;
    }

    private void ApplyManagementAuth(HttpRequestMessage request)
    {
        if (_jwt is null) return;
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _jwt.GetBearerToken());
    }
    
    public override async Task<CallResult> MakeCallAsync(MakeCallRequest request)
    {
        if (_config == null)
            return new CallResult { Success = false, ErrorMessage = "Provider not configured" };

        var ruleId = _config.IncidentCallRuleId ?? 0;
        if (ruleId <= 0)
        {
            return new CallResult
            {
                Success = false,
                ErrorMessage = "Voximplant incident-call rule is not provisioned. Open Communications → Manage and complete provisioning (application + scenario + rule)."
            };
        }
            
        try
        {
            var client = httpClientFactory.CreateClient("Voximplant");
            var customData = request.CustomData ?? "";

            if (request.IncidentId.HasValue)
            {
                var callData = await BuildCallDataAsync(request);
                var token = await callDataService.CreateCallTokenAsync(callData);
                customData = JsonSerializer.Serialize(new { call_token = token });
                logger.LogInformation("Created call token for incident {IncidentId}, calling {Phone}",
                    request.IncidentId, request.Destination);
            }
            
            var url = BuildPlatformUrl("StartScenarios", new Dictionary<string, string>
            {
                ["rule_id"] = ruleId.ToString(),
                ["script_custom_data"] = customData
            });
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyManagementAuth(httpRequest);
            var response = await client.SendAsync(httpRequest);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<VoximplantStartScenariosResponse>();
                if (result?.Result == 1)
                {
                    return new CallResult
                    {
                        Success = true,
                        CallId = result.MediaSessionAccessUrl,
                        SessionUrl = result.MediaSessionAccessUrl
                    };
                }
                // The platform answered and said no, so nothing was started and a retry can follow.
                return new CallResult { Success = false, ErrorMessage = result?.Error?.Msg ?? "Unknown error" };
            }

            if (VoximplantDialAnswers.ForStatusCode((int)response.StatusCode) == VoximplantDialAnswer.Refused)
                return new CallResult { Success = false, ErrorMessage = $"HTTP error: {response.StatusCode}" };

            // Thrown, not returned false: the scenario may already be running and the phone ringing,
            // so the caller has to treat this as "cannot tell" rather than re-dial straight away.
            throw new HttpRequestException(
                $"Voximplant answered HTTP {(int)response.StatusCode} to StartScenarios for incident "
                + $"{request.IncidentId}; whether the call was placed is unknown.");
        }
        catch (HttpRequestException ex) when (VoximplantDialAnswers.NeverReachedTheService(ex.HttpRequestError))
        {
            logger.LogWarning(ex,
                "Voximplant could not be reached for incident {IncidentId}; no call was placed",
                request.IncidentId);
            return new CallResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException(
                $"Voximplant did not answer StartScenarios for incident {request.IncidentId}; "
                + "whether the call was placed is unknown.", ex);
        }
    }
    
    /// <summary>
    /// Build VoxCallData from MakeCallRequest incident context + SIP trunk + TTS templates
    /// </summary>
    private async Task<VoxCallData> BuildCallDataAsync(MakeCallRequest request)
    {
        // The prompts are spoken to the person being called, so they follow that person's language and
        // never the incident's. Falling back to DataLanguage here is what made every call English:
        // the incident text defaults to en-US, and the prompts followed it.
        // Not restricted to a fixed list: an operator can add a language by saving a TTS template,
        // and ResolveMessagesAsync already falls back to the default template for anything unknown.
        var language = string.IsNullOrWhiteSpace(request.Language)
            ? SupportedCultures.Fallback
            : request.Language;
        // The resolved language, not the requested one: when resolution falls back to another
        // language's template, the voice has to match the words it is given.
        var resolved = await ttsTemplateService.ResolveMessagesAsync(language);
        var ttsMessages = resolved.Messages;

        string WrapLang(string? value, string langCode)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var safe = System.Security.SecurityElement.Escape(value) ?? string.Empty;
            var safeLang = System.Security.SecurityElement.Escape(langCode) ?? "en-US";
            return $"<lang xml:lang=\"{safeLang}\">{safe}</lang>";
        }

        // The incident text keeps its own language: a Turkish alert stays Turkish even when the
        // responder is being prompted in English.
        var dataLang = string.IsNullOrWhiteSpace(request.DataLanguage)
            ? SupportedCultures.Fallback
            : request.DataLanguage;

        // Not wrapped in the data language: the severity is menu vocabulary, so it is spoken to the
        // person in their own language rather than the alert's.
        var severityWord = TtsDefaults.SeverityWord(ttsMessages, request.Severity);

        var resolvedMessages = ttsMessages.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                    .Replace("{title}", WrapLang(request.IncidentTitle ?? "Alert", dataLang))
                    .Replace("{severity_text}", severityWord)
                    .Replace("{severity}", WrapLang(request.Severity ?? "Medium", dataLang))
                    .Replace("{service}", WrapLang(request.ServiceName ?? "Service", dataLang))
                    .Replace("{description}", WrapLang(TtsDefaults.ClipDescription(request.Description), dataLang)));

        var callData = new VoxCallData
        {
            IncidentId = request.IncidentId?.ToString() ?? "",
            AttemptId = request.AttemptId?.ToString("N"),
            Title = request.IncidentTitle ?? "Incident Alert",
            Severity = request.Severity ?? "Medium",
            ServiceName = request.ServiceName ?? "",
            Description = request.Description ?? "",
            Phone = request.Destination,
            Language = resolved.LanguageCode,
            TtsMessages = resolvedMessages,
        };

        if (SipTrunk != null)
        {
            callData.SipServer = SipTrunk.Server;
            callData.SipUsername = SipTrunk.Username;
            callData.SipPassword = sipTrunkProtector.Unprotect(SipTrunk.Password);
            callData.CallerId = SipTrunk.CallerId ?? _config?.DefaultCallerId ?? "";
        }
        
        return callData;
    }


    public override async Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request)
    {
        if (_config == null)
            return new ConferenceResult { Success = false, ErrorMessage = "Provider not configured" };

        var ruleId = _config.ConferenceRuleId ?? 0;
        if (ruleId <= 0)
        {
            return new ConferenceResult
            {
                Success = false,
                ErrorMessage = "Voximplant conference rule is not provisioned. Open Communications → Manage and complete provisioning."
            };
        }
            
        try
        {
            var client = httpClientFactory.CreateClient("Voximplant");
            
            var customData = JsonSerializer.Serialize(new
            {
                conferenceName = request.Name,
                participants = request.Participants,
                enableVideo = request.EnableVideo,
                enableRecording = request.EnableRecording
            });
            
            var url = BuildPlatformUrl("StartConference", new Dictionary<string, string>
            {
                ["conference_name"] = request.Name,
                ["rule_id"] = ruleId.ToString(),
                ["script_custom_data"] = customData
            });
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyManagementAuth(httpRequest);
            var response = await client.SendAsync(httpRequest);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<VoximplantStartScenariosResponse>();
                return new ConferenceResult
                {
                    Success = result?.Result == 1,
                    ConferenceId = request.Name,
                    JoinUrl = result?.MediaSessionAccessUrl,
                    ErrorMessage = result?.Error?.Msg
                };
            }
            
            return new ConferenceResult { Success = false, ErrorMessage = $"HTTP error: {response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to create Voximplant conference");
            return new ConferenceResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public void Dispose()
    {
        _jwt?.Dispose();
    }
}
