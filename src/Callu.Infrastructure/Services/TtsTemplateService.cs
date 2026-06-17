using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for managing per-language TTS message templates.
/// Provides CRUD operations and language resolution with JSON-based fallback defaults.
/// </summary>
public class TtsTemplateService(
    ITtsMessageTemplateRepository templateRepo,
    ITransactionManager transactionManager,
    HybridCache cache,
    ILogger<TtsTemplateService> logger) : ITtsTemplateService
{
    private static string TtsCacheKey(string languageCode) => $"tts-messages:{languageCode}";

    public async Task<List<TtsTemplateDto>> GetAllAsync(CancellationToken ct = default)
    {
        var templates = await templateRepo.GetQueryable()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.LanguageCode)
            .ToListAsync(ct);

        return templates.Select(MapToDto).ToList();
    }

    public async Task<TtsTemplateDto?> GetByLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        var template = await templateRepo.FindSingleAsync(
            t => t.LanguageCode == languageCode && !t.IsDeleted, ct);

        return template != null ? MapToDto(template) : null;
    }

    public async Task SaveAsync(TtsTemplateSaveRequest request, CancellationToken ct = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await templateRepo.FindSingleAsync(
                t => t.LanguageCode == request.LanguageCode && !t.IsDeleted, ct);

            if (existing != null)
            {
                existing.DisplayName = request.DisplayName;
                existing.IsDefault = request.IsDefault;
                existing.MessagesJson = JsonSerializer.Serialize(request.Messages);
                templateRepo.Update(existing);
            }
            else
            {
                await templateRepo.AddAsync(new TtsMessageTemplate
                {
                    LanguageCode = request.LanguageCode,
                    DisplayName = request.DisplayName,
                    IsDefault = request.IsDefault,
                    MessagesJson = JsonSerializer.Serialize(request.Messages)
                }, ct);
            }

            if (request.IsDefault)
            {
                var others = await templateRepo.FindAsync(
                    t => t.LanguageCode != request.LanguageCode && t.IsDefault && !t.IsDeleted, ct);

                foreach (var other in others)
                {
                    other.IsDefault = false;
                    templateRepo.Update(other);
                }
            }

            logger.LogInformation("TTS template saved for language {Lang}", request.LanguageCode);
            await cache.RemoveAsync(TtsCacheKey(request.LanguageCode), ct);
        }, ct);
    }

    public async Task DeleteAsync(string languageCode, CancellationToken ct = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await templateRepo.FindSingleAsync(
                t => t.LanguageCode == languageCode && !t.IsDeleted, ct);

            if (template != null)
            {
                template.IsDeleted = true;
                templateRepo.Update(template);
                logger.LogInformation("TTS template deleted for language {Lang}", languageCode);
                await cache.RemoveAsync(TtsCacheKey(languageCode), ct);
            }
        }, ct);
    }

    public async Task<Dictionary<string, string>> ResolveMessagesAsync(string languageCode, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            TtsCacheKey(languageCode),
            async innerCt => await ResolveMessagesInternalAsync(languageCode, innerCt),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) },
            cancellationToken: ct);
    }

    private async Task<Dictionary<string, string>> ResolveMessagesInternalAsync(string languageCode, CancellationToken ct)
    {
        var resolved = TtsDefaults.GetDefaults(languageCode);

        var template = await templateRepo.FindSingleAsync(
            t => t.LanguageCode == languageCode && !t.IsDeleted, ct);

        if (template == null)
        {
            template = await templateRepo.FindSingleAsync(
                t => t.IsDefault && !t.IsDeleted, ct);
        }

        if (template != null)
        {
            try
            {
                var dbMessages = JsonSerializer.Deserialize<Dictionary<string, string>>(template.MessagesJson);
                if (dbMessages != null)
                {
                    foreach (var kvp in dbMessages)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                            resolved[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse TTS messages for {Lang}, using defaults", languageCode);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Returns the built-in default messages (English).
    /// </summary>
    public static Dictionary<string, string> GetStaticDefaultMessages() => TtsDefaults.GetEnglishDefaults();

    /// <inheritdoc/>
    Dictionary<string, string> ITtsTemplateService.GetDefaultMessages() => TtsDefaults.GetEnglishDefaults();

    /// <inheritdoc/>
    public Dictionary<string, string> GetDefaultsForLanguage(string languageCode) => TtsDefaults.GetDefaults(languageCode);

    private static TtsTemplateDto MapToDto(TtsMessageTemplate entity)
    {
        Dictionary<string, string> messages;
        try
        {
            messages = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MessagesJson) ?? new();
        }
        catch
        {
            messages = new();
        }

        return new TtsTemplateDto
        {
            Id = entity.Id,
            LanguageCode = entity.LanguageCode,
            DisplayName = entity.DisplayName,
            IsDefault = entity.IsDefault,
            Messages = messages
        };
    }
}
