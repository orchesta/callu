using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Communication;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for managing communication providers
/// </summary>
public class CommunicationProviderService : ICommunicationProviderService
{
    private readonly ICommunicationProviderRepository _providerRepo;
    private readonly ICapabilityProviderMappingRepository _routeRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly ICommunicationProviderRegistry _registry;
    private readonly ProviderSecretProtector _secretProtector;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CommunicationProviderService> _logger;

    public CommunicationProviderService(
        ICommunicationProviderRepository providerRepo,
        ICapabilityProviderMappingRepository routeRepo,
        ITransactionManager transactionManager,
        ICommunicationProviderRegistry registry,
        ProviderSecretProtector secretProtector,
        IAuditLogService auditLogService,
        ILogger<CommunicationProviderService> logger)
    {
        _providerRepo = providerRepo;
        _routeRepo = routeRepo;
        _transactionManager = transactionManager;
        _registry = registry;
        _secretProtector = secretProtector;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>Names the secret keys a request touched, never their values.</summary>
    // An audit row is read by more people than the config screen is, so the secret itself never
    // reaches it — only the fact that it moved.
    private static string DescribeSecretChange(IReadOnlyDictionary<string, object>? config, string providerType)
    {
        if (config is null || !SecretConfigKeys.TryGetValue(providerType, out var secretKeys))
            return "no secret fields touched";

        var touched = secretKeys.Where(config.ContainsKey).ToList();
        return touched.Count == 0 ? "no secret fields touched" : $"secrets replaced: {string.Join(", ", touched)}";
    }

    private static readonly Dictionary<string, string[]> SecretConfigKeys = new()
    {
        ["voximplant"] = ["apiKey", "serviceAccountJson"],
        ["verimor"] = ["apiUsername", "apiPassword"],
        ["http-sms"] = ["apiKey", "username", "password"],
        ["callu-voice"] = ["apiToken"],
    };
    
    public async Task<IEnumerable<CommunicationProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        var providers = await _providerRepo.GetQueryable()
            .Where(p => !p.IsDeleted)
            .Include(p => p.SipTrunk)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
                
        return providers.Select(p => EnrichFromConfig(p, p.Adapt<CommunicationProviderDto>()));
    }
    
    public async Task<CommunicationProviderDto?> GetProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var provider = await _providerRepo.GetWithSipTrunkAsync(id, cancellationToken);
        if (provider == null) return null;
        
        return EnrichFromConfig(provider, provider.Adapt<CommunicationProviderDto>());
    }
    
    public async Task<CommunicationProviderDto> CreateProviderAsync(CreateProviderRequest request, CancellationToken cancellationToken = default)
    {
        var availableTypes = _registry.GetAvailableProviderTypes();
        if (!availableTypes.Contains(request.ProviderType))
        {
            throw new ArgumentException($"Unknown provider type: {request.ProviderType}");
        }

        RefuseIfItCannotReportBack(request.ProviderType, request.Config);
        await RefuseIfAnotherProviderOwnsTheSameVoiceServiceAsync(
            null, request.ProviderType, request.Config, cancellationToken);

        var configJson = EncryptSensitiveConfig(request.Config, request.ProviderType);
        
        var providerId = await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var provider = new CommunicationProvider
            {
                Name = request.Name,
                ProviderType = request.ProviderType,
                ConfigJson = configJson,
                SipTrunkId = request.SipTrunkId,
                Priority = request.Priority,
                IsEnabled = true
            };
            
            provider.Capabilities = request.ProviderType switch
            {
                "voximplant" => CommunicationCapability.VoiceCalls | CommunicationCapability.VideoConference | 
                                CommunicationCapability.TTS | 
                                CommunicationCapability.ASR |
                                CommunicationCapability.Recording | CommunicationCapability.VoicemailDetection,
                "verimor" => CommunicationCapability.Sms,
                "http-sms" => CommunicationCapability.Sms,
                "callu-voice" => CommunicationCapability.VoiceCalls,
                _ => CommunicationCapability.None
            };
            
            await _providerRepo.AddAsync(provider, cancellationToken);
            
            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "CommunicationProvider", provider.Id.ToString(),
                newValues: $"name={provider.Name}; type={provider.ProviderType}; priority={provider.Priority}",
                description: DescribeSecretChange(request.Config, provider.ProviderType),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created communication provider: {Name} ({Type})", 
                provider.Name, provider.ProviderType);
            
            return provider.Id;
        }, cancellationToken);
        
        await _registry.ReloadProvidersAsync(cancellationToken);
        // A brand new provider has had no carrier, so leaving the selector alone is not a removal.
        if (request.SipTrunkId is not null)
            await PushTrunkIfSelfHostedVoiceAsync(providerId, cancellationToken);

        return (await GetProviderAsync(providerId, cancellationToken))!;
    }
    
    public async Task UpdateProviderAsync(Guid id, UpdateProviderRequest request, CancellationToken cancellationToken = default)
    {
        // Read inside the transaction, used after it: telling "no trunk chosen" from "the trunk this
        // provider had, unlinked" is the whole of whether a save may remove a carrier.
        Guid? trunkBefore = null;

        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (provider == null)
                throw new KeyNotFoundException($"Provider not found: {id}");

            trunkBefore = provider.SipTrunkId;
            var before = $"name={provider.Name}; enabled={provider.IsEnabled}; priority={provider.Priority}";

            // The whole config this request would leave behind, so the gate below judges the stored
            // keys as well as the ones being written.
            var mergedConfig = MergeConfig(provider.ConfigJson, request.Config);

            if (request.IsEnabled)
            {
                RefuseIfItCannotReportBack(provider.ProviderType, mergedConfig);
                await RefuseIfAnotherProviderOwnsTheSameVoiceServiceAsync(
                    provider.Id, provider.ProviderType, mergedConfig, cancellationToken);
            }

            if (!string.IsNullOrEmpty(request.Name))
                provider.Name = request.Name;

            provider.IsEnabled = request.IsEnabled;
            provider.Priority = request.Priority;
            // Outside the config guard: a request that changes only the carrier would otherwise be
            // answered with 200 and write nothing.
            provider.SipTrunkId = request.SipTrunkId;

            if (request.Config != null)
            {
                provider.ConfigJson = EncryptSensitiveConfig(mergedConfig, provider.ProviderType);
            }

            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "CommunicationProvider", provider.Id.ToString(),
                oldValues: before,
                newValues: $"name={provider.Name}; enabled={provider.IsEnabled}; priority={provider.Priority}",
                description: DescribeSecretChange(request.Config, provider.ProviderType),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated communication provider: {Name}", provider.Name);
            return true;
        }, cancellationToken);

        await _registry.ReloadProvidersAsync(cancellationToken);

        // No trunk chosen means "I have not told you which carrier", not "take the one you have
        // away" — the voice service is only told to drop a carrier when the trunk the provider
        // previously had is explicitly unlinked.
        if (request.SipTrunkId is not null)
            await PushTrunkIfSelfHostedVoiceAsync(id, cancellationToken);
        else if (trunkBefore is not null)
            await ClearTrunkIfSelfHostedVoiceAsync(id, cancellationToken);
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Before the row goes: once it has, the registry holds no instance to tell the voice
        // service with, and it would keep registering with credentials Callu no longer routes to.
        if (await _registry.GetConfiguredProviderAsync(id) is CalluVoiceProvider voice)
            await RecordCarrierPushAsync(id, await voice.ClearTrunkAsync(), cancellationToken);

        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (provider == null)
                throw new KeyNotFoundException($"Provider not found: {id}");
            
            provider.IsDeleted = true;

            await _auditLogService.LogAsync(
                null, AuditAction.Deleted, "CommunicationProvider", provider.Id.ToString(),
                oldValues: $"name={provider.Name}; type={provider.ProviderType}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted communication provider: {Name}", provider.Name);
            return true;
        }, cancellationToken);
        
        await _registry.ReloadProvidersAsync(cancellationToken);
    }
    
    public async Task<SmsResult> SendTestSmsAsync(Guid id, string to, string? message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            return new SmsResult { Success = false, ErrorMessage = "Destination number is required." };

        var provider = await _registry.GetConfiguredProviderAsync(id);
        if (provider == null)
            return new SmsResult { Success = false, ErrorMessage = "Provider not loaded. Enable it and try again." };

        if (!provider.Capabilities.HasFlag(Callu.Domain.Enums.CommunicationCapability.Sms))
            return new SmsResult { Success = false, ErrorMessage = "This provider does not support SMS." };

        var result = await provider.SendSmsAsync(new SendSmsRequest
        {
            To = to.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? "Callu test message." : message!.Trim()
        });

        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await _providerRepo.FindSingleAsync(p => p.Id == id, cancellationToken);
            if (entity != null)
            {
                entity.LastTestedAt = DateTime.UtcNow;
                entity.LastTestResult = result.Success ? "Test SMS sent" : $"Test SMS failed: {result.ErrorMessage}";
            }
            return true;
        }, cancellationToken);

        return result;
    }

    /// <summary>The stored config with this request's keys written over it.</summary>
    private Dictionary<string, object> MergeConfig(string? storedJson, IReadOnlyDictionary<string, object>? incoming)
    {
        var merged = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(storedJson))
        {
            try
            {
                var current = JsonSerializer.Deserialize<Dictionary<string, object>>(storedJson);
                if (current != null) merged = current;
            }
            catch (JsonException ex) { _logger.LogDebug(ex, "Failed to parse existing provider config during merge"); }
        }

        if (incoming != null)
        {
            foreach (var kvp in incoming)
                merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }

    /// <summary>Refuses a provider whose configuration leaves it no way to tell Callu what happened on a call.</summary>
    // A voice provider that places calls but reports none is worse than none at all: the responder is told
    // the incident is acknowledged, it stays open, and no further attempt is ever made.
    private static void RefuseIfItCannotReportBack(
        string providerType, IReadOnlyDictionary<string, object>? config)
    {
        if (providerType != Providers.CalluVoice.CalluVoiceProvider.TypeName) return;

        object? configured = null;
        config?.TryGetValue(Providers.CalluVoice.CalluVoiceConfig.CallbackUrlKey, out configured);

        if (Providers.CalluVoice.CalluVoiceConfig.RefuseCallbackUrl(ConfigValueAsString(configured)) is { } refusal)
            throw new Shared.Exceptions.ValidationException(refusal);
    }

    /// <summary>Refuses a second enabled provider pointing at a voice service another one already owns.</summary>
    // A voice service holds one carrier, so two providers naming it would each push their own and
    // the reconciliation sweep would replace one with the other every few minutes.
    private async Task RefuseIfAnotherProviderOwnsTheSameVoiceServiceAsync(
        Guid? self, string providerType, IReadOnlyDictionary<string, object>? config, CancellationToken cancellationToken)
    {
        if (providerType != CalluVoiceProvider.TypeName) return;

        object? raw = null;
        config?.TryGetValue(CalluVoiceConfig.BaseUrlKey, out raw);

        if (CalluVoiceConfig.VoiceServiceKey(ConfigValueAsString(raw)) is not { } key) return;

        var rivals = await _providerRepo.FindAsync(
            p => !p.IsDeleted
                 && p.IsEnabled
                 && p.ProviderType == CalluVoiceProvider.TypeName
                 && (self == null || p.Id != self),
            cancellationToken);

        // Ordered so the refusal names the same provider every time it is triggered.
        var clash = rivals
            .Where(r => string.Equals(
                CalluVoiceConfig.VoiceServiceKeyFromJson(r.ConfigJson), key, StringComparison.Ordinal))
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id)
            .FirstOrDefault();

        if (clash is null) return;

        throw new Shared.Exceptions.ValidationException(
            $"'{clash.Name}' already points at the voice service at {key}. A voice service holds one SIP "
            + "carrier, so a second provider on the same address would push its own and Callu would replace "
            + "one with the other every few minutes — a page placed while the carrier is being swapped "
            + "reaches nobody. Point this provider at a different callu-voice instance, or switch off "
            + $"'{clash.Name}' first.");
    }

    /// <summary>Encrypts the known secret fields of a provider config in place, then serializes.</summary>
    // Idempotent — already-encrypted values are not re-wrapped; provisioning.scenarioApiKey is owned by the lifecycle.
    private string EncryptSensitiveConfig(Dictionary<string, object> config, string providerType)
    {
        if (SecretConfigKeys.TryGetValue(providerType, out var secretKeys))
        {
            foreach (var key in secretKeys)
            {
                if (!config.TryGetValue(key, out var raw)) continue;
                var plain = ConfigValueAsString(raw);
                if (string.IsNullOrEmpty(plain)) continue;
                config[key] = _secretProtector.Protect(plain);
            }
        }
        return JsonSerializer.Serialize(config);
    }

    private static string? ConfigValueAsString(object? raw) => raw switch
    {
        null => null,
        string s => s,
        JsonElement el => el.ValueKind == JsonValueKind.String ? el.GetString() : null,
        _ => raw.ToString(),
    };
    
    /// <summary>
    /// Parse ConfigJson and populate provider-specific DTO fields that Mapster can't resolve from entity convention.
    /// </summary>
    private static CommunicationProviderDto EnrichFromConfig(CommunicationProvider entity, CommunicationProviderDto dto)
    {
        if (string.IsNullOrEmpty(entity.ConfigJson)) return dto;

        try
        {
            var cfg = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entity.ConfigJson);
            if (cfg == null) return dto;

            if (entity.ProviderType == "voximplant")
            {
                return dto with
                {
                    VoximplantAccountId = TryGetString(cfg, "accountId"),
                    VoximplantNode = TryGetString(cfg, "node"),
                    VoximplantApplicationId = TryGetLong(cfg, "provisionedApplicationId"),
                    VoximplantApplicationName = TryGetString(cfg, "provisionedApplicationName"),
                    VoximplantScenarioId = TryGetLong(cfg, "provisionedScenarioId"),
                    VoximplantScenarioName = TryGetString(cfg, "provisionedScenarioName"),
                    VoximplantRuleId = TryGetLong(cfg, "provisionedRuleId"),
                    VoximplantRuleName = TryGetString(cfg, "provisionedRuleName"),
                };
            }

            if (entity.ProviderType == "callu-voice")
            {
                return dto with
                {
                    CalluVoice = new CalluVoiceConfigDto
                    {
                        BaseUrl = TryGetString(cfg, "baseUrl") ?? string.Empty,
                        CallbackUrl = TryGetString(cfg, CalluVoiceConfig.CallbackUrlKey),
                        Voice = TryGetString(cfg, "voice"),
                        RequestTimeoutSeconds = (int?)TryGetLong(cfg, "requestTimeoutSeconds"),
                        HasApiToken = HasNonEmpty(cfg, "apiToken"),
                    },
                };
            }

            if (entity.ProviderType == "http-sms")
            {
                return dto with
                {
                    HttpSms = new HttpSmsConfigDto
                    {
                        Url = TryGetString(cfg, "url") ?? string.Empty,
                        Method = TryGetString(cfg, "method") ?? "POST",
                        ContentType = TryGetString(cfg, "contentType") ?? "json",
                        SenderId = TryGetString(cfg, "senderId"),
                        BodyTemplate = TryGetString(cfg, "bodyTemplate"),
                        Headers = TryGetStringMap(cfg, "headers"),
                        SuccessMode = TryGetString(cfg, "successMode"),
                        SuccessField = TryGetString(cfg, "successField"),
                        SuccessValue = TryGetString(cfg, "successValue"),
                        MessageIdPath = TryGetString(cfg, "messageIdPath"),
                        HasApiKey = HasNonEmpty(cfg, "apiKey"),
                        HasUsername = HasNonEmpty(cfg, "username"),
                        HasPassword = HasNonEmpty(cfg, "password"),
                    }
                };
            }

            return dto;
        }
        catch (JsonException)
        {
            return dto;
        }
    }

    private static Dictionary<string, string>? TryGetStringMap(Dictionary<string, JsonElement> cfg, string key)
    {
        if (!cfg.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        var map = new Dictionary<string, string>();
        foreach (var prop in el.EnumerateObject())
            map[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
        return map.Count > 0 ? map : null;
    }

    private static bool HasNonEmpty(Dictionary<string, JsonElement> cfg, string key) =>
        cfg.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString());
    
    private static string? TryGetString(Dictionary<string, JsonElement> cfg, string key)
    {
        if (!cfg.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString()
             : el.ValueKind != JsonValueKind.Null ? el.ToString()
             : null;
    }
    
    private static long? TryGetLong(Dictionary<string, JsonElement> cfg, string key)
    {
        if (!cfg.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetInt64()
             : el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var v) ? v
             : null;
    }

    /// <summary>Hands the carrier to a self-hosted voice service, which holds none of its own.</summary>
    // Only when an operator saves: the service keeps the last carrier it was given, so a failure
    // here leaves calls working on the previous one rather than stopping the save.
    private async Task PushTrunkIfSelfHostedVoiceAsync(Guid providerId, CancellationToken cancellationToken)
    {
        if (await _registry.GetConfiguredProviderAsync(providerId) is not CalluVoiceProvider voice)
            return;

        await RecordCarrierPushAsync(providerId, await voice.ApplyTrunkAsync(), cancellationToken);
    }

    /// <summary>Takes the carrier off a voice service whose trunk an operator has just unlinked.</summary>
    private async Task ClearTrunkIfSelfHostedVoiceAsync(Guid providerId, CancellationToken cancellationToken)
    {
        if (await _registry.GetConfiguredProviderAsync(providerId) is not CalluVoiceProvider voice)
            return;

        await RecordCarrierPushAsync(providerId, await voice.ClearTrunkAsync(), cancellationToken);
    }

    /// <summary>Writes the outcome of a carrier push where an operator can find it.</summary>
    // The save itself succeeded, so nothing on the screen would otherwise say the voice service
    // is still dialling through the previous carrier.
    private async Task RecordCarrierPushAsync(
        Guid providerId, (bool Applied, string? Error) outcome, CancellationToken cancellationToken)
    {
        if (outcome.Applied)
            return;

        _logger.LogError(
            "The self-hosted voice provider was saved but its carrier was not applied: {Error}. "
            + "Calls will use whatever carrier the voice service already had.", outcome.Error);

        await _auditLogService.LogAsync(
            null, AuditAction.SettingsChanged, "CommunicationProvider", providerId.ToString(),
            description: $"Carrier was NOT applied to the voice service: {Clip(outcome.Error)}",
            cancellationToken: cancellationToken);
    }

    // Leaves room under AuditLog.Description for the sentence this is embedded in.
    private const int MaxCarrierErrorChars = 400;

    private static string Clip(string? error)
    {
        var text = (error ?? "unknown error").Trim();
        return text.Length <= MaxCarrierErrorChars ? text : text[..MaxCarrierErrorChars];
    }

    /// <summary>Every capability that can be routed, with what it is pinned to and what it could be.</summary>
    public async Task<IEnumerable<CapabilityRouteDto>> GetCapabilityRoutesAsync(CancellationToken cancellationToken = default)
    {
        // A read, so it must not hand back whatever the change tracker happens to be holding.
        var providers = await _providerRepo.GetQueryable().AsNoTracking().ToListAsync(cancellationToken);
        var routes = await _routeRepo.GetQueryable().AsNoTracking().ToListAsync(cancellationToken);

        var result = new List<CapabilityRouteDto>();

        foreach (var capability in RoutableCapabilities)
        {
            var route = routes
                .Where(r => r.Capability == capability && r.IsEnabled)
                .OrderBy(r => r.Priority).ThenBy(r => r.Id)
                .FirstOrDefault();

            var target = route is null
                ? null
                : providers.FirstOrDefault(p => p.Id == route.ProviderId);

            var candidates = providers
                .Where(p => p.Capabilities.HasFlag(capability))
                .OrderBy(p => p.Priority).ThenBy(p => p.Name)
                .Select(p => new CapabilityRouteCandidateDto
                {
                    ProviderId = p.Id,
                    Name = p.Name,
                    ProviderType = p.ProviderType,
                    IsEnabled = p.IsEnabled,
                })
                .ToList();

            // A channel no configured provider can carry offers no choice, so it is not a decision
            // to put in front of an operator — unless something is already pinned to it, in which
            // case hiding it would hide a route whose provider has gone.
            if (candidates.Count == 0 && target is null) continue;

            result.Add(new CapabilityRouteDto
            {
                Capability = capability,
                ProviderId = target?.Id,
                ProviderName = target?.Name,
                ProviderType = target?.ProviderType,
                IsProviderEnabled = target?.IsEnabled ?? false,
                Candidates = candidates,
            });
        }

        return result;
    }

    /// <summary>Pins a capability to one provider, or clears the pin when the provider is null.</summary>
    public async Task SetCapabilityRouteAsync(
        CommunicationCapability capability, Guid? providerId, CancellationToken cancellationToken = default)
    {
        if (!RoutableCapabilities.Contains(capability))
            throw new BusinessRuleException($"'{capability}' is not a routable capability.");

        CommunicationProvider? target = null;
        if (providerId is { } id)
        {
            target = await _providerRepo.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException("CommunicationProvider", id);

            // Routing a capability to a provider that cannot do it takes the channel away from the
            // ones that can and gives it to nobody.
            if (!target.Capabilities.HasFlag(capability))
                throw new BusinessRuleException(
                    $"'{target.Name}' does not support {capability}, so it cannot be given that channel.");
        }

        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await _routeRepo.GetQueryable()
                .Where(r => r.Capability == capability)
                .ToListAsync(cancellationToken);

            foreach (var stale in existing)
                _routeRepo.Remove(stale);

            if (target is not null)
            {
                await _routeRepo.AddAsync(new CapabilityProviderMapping
                {
                    Capability = capability,
                    ProviderId = target.Id,
                    Priority = 0,
                    IsEnabled = true,
                }, cancellationToken);
            }

            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "CapabilityRoute", capability.ToString(),
                oldValues: existing.Count == 0 ? "unrouted" : string.Join(", ", existing.Select(e => e.ProviderId)),
                newValues: target is null ? "unrouted" : $"{target.Name} ({target.ProviderType})",
                description: target is null
                    ? $"{capability} follows the ordinary provider order again"
                    : $"{capability} routed to {target.Name}",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);

        await _registry.ReloadProvidersAsync(cancellationToken);
    }

    /// <summary>The capabilities an operator can hand to a named provider.</summary>
    // Flags that describe how a provider does its job rather than a channel it owns are left out.
    private static readonly CommunicationCapability[] RoutableCapabilities =
    [
        CommunicationCapability.VoiceCalls,
        CommunicationCapability.Sms,
        CommunicationCapability.WhatsApp,
        CommunicationCapability.VideoConference,
    ];
}
