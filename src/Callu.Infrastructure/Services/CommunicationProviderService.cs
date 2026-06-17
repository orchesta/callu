using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers;
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
    private readonly ITransactionManager _transactionManager;
    private readonly ICommunicationProviderRegistry _registry;
    private readonly ProviderSecretProtector _secretProtector;
    private readonly ILogger<CommunicationProviderService> _logger;

    public CommunicationProviderService(
        ICommunicationProviderRepository providerRepo,
        ITransactionManager transactionManager,
        ICommunicationProviderRegistry registry,
        ProviderSecretProtector secretProtector,
        ILogger<CommunicationProviderService> logger)
    {
        _providerRepo = providerRepo;
        _transactionManager = transactionManager;
        _registry = registry;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    private static readonly Dictionary<string, string[]> SecretConfigKeys = new()
    {
        ["voximplant"] = ["apiKey", "serviceAccountJson"],
        ["http-sms"] = ["apiKey", "username", "password"],
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
                _ => CommunicationCapability.None
            };
            
            await _providerRepo.AddAsync(provider, cancellationToken);
            
            _logger.LogInformation("Created communication provider: {Name} ({Type})", 
                provider.Name, provider.ProviderType);
            
            return provider.Id;
        }, cancellationToken);
        
        await _registry.ReloadProvidersAsync(cancellationToken);
        
        return (await GetProviderAsync(providerId, cancellationToken))!;
    }
    
    public async Task UpdateProviderAsync(Guid id, UpdateProviderRequest request, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (provider == null)
                throw new KeyNotFoundException($"Provider not found: {id}");
            
            if (!string.IsNullOrEmpty(request.Name))
                provider.Name = request.Name;
            
            provider.IsEnabled = request.IsEnabled;
            provider.Priority = request.Priority;
            
            if (request.Config != null)
            {
                provider.SipTrunkId = request.SipTrunkId;

                var mergedConfig = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(provider.ConfigJson))
                {
                    try
                    {
                        var current = JsonSerializer.Deserialize<Dictionary<string, object>>(provider.ConfigJson);
                        if (current != null) mergedConfig = current;
                    }
                    catch (JsonException ex) { _logger.LogDebug(ex, "Failed to parse existing provider config during merge"); }
                }

                foreach (var kvp in request.Config)
                {
                    mergedConfig[kvp.Key] = kvp.Value;
                }

                provider.ConfigJson = EncryptSensitiveConfig(mergedConfig, provider.ProviderType);
            }
            
            _logger.LogInformation("Updated communication provider: {Name}", provider.Name);
            return true;
        }, cancellationToken);
        
        await _registry.ReloadProvidersAsync(cancellationToken);
    }
    
    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (provider == null)
                throw new KeyNotFoundException($"Provider not found: {id}");
            
            provider.IsDeleted = true;
            
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

    /// <summary>
    /// Encrypts the known secret fields of a provider config in-place (field-level), preserving
    /// the JSON shape, then serializes. Idempotent: values already encrypted (e.g. preserved
    /// through the update merge) are not re-wrapped. Non-secret fields are untouched, and nested
    /// provisioning.scenarioApiKey is left to the lifecycle which owns its read/write path.
    /// </summary>
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
}
