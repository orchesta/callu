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
        }, ct);

        await InvalidateAfterCommitAsync(request.LanguageCode, ct);
    }

    public async Task DeleteAsync(string languageCode, CancellationToken ct = default)
    {
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await templateRepo.FindSingleAsync(
                t => t.LanguageCode == languageCode && !t.IsDeleted, ct);

            if (template == null)
                return false;

            template.IsDeleted = true;
            templateRepo.Update(template);
            logger.LogInformation("TTS template deleted for language {Lang}", languageCode);
            return true;
        }, ct);

        if (deleted)
            await InvalidateAfterCommitAsync(languageCode, ct);
    }

    /// <summary>
    /// Drops the cached messages for a language, always after the transaction has committed.
    /// </summary>
    private Task InvalidateAfterCommitAsync(string languageCode, CancellationToken ct) =>
        cache.RemoveAsync(TtsCacheKey(languageCode), ct).AsTask();

    public async Task<TtsResolvedMessages> ResolveMessagesAsync(string languageCode, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            TtsCacheKey(languageCode),
            async innerCt => await ResolveMessagesInternalAsync(languageCode, innerCt),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) },
            cancellationToken: ct);
    }

    private async Task<TtsResolvedMessages> ResolveMessagesInternalAsync(string languageCode, CancellationToken ct)
    {
        var resolved = TtsDefaults.GetDefaults(languageCode);

        // Only moves off the requested language once this template's text is actually in the mix:
        // an unreadable or empty template leaves the built-in defaults, which are the language asked for.
        var spokenLanguage = languageCode;

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
                    var applied = 0;
                    foreach (var kvp in dbMessages)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Value)) continue;
                        resolved[kvp.Key] = kvp.Value;
                        applied++;
                    }

                    if (applied > 0 && !string.IsNullOrWhiteSpace(template.LanguageCode))
                        spokenLanguage = template.LanguageCode;
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse TTS messages for {Lang}, using defaults", languageCode);
            }
        }

        if (!string.Equals(spokenLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            logger.LogInformation(
                "No TTS template for {Requested}; falling back to the default template, so the call is spoken "
                + "in {Spoken} and the synthesizer is told so.",
                languageCode, spokenLanguage);

        return new TtsResolvedMessages(spokenLanguage, resolved);
    }

    /// <summary>
    /// Returns the built-in default messages (English).
    /// </summary>
    public static Dictionary<string, string> GetStaticDefaultMessages() => TtsDefaults.GetEnglishDefaults();

    /// <inheritdoc/>
    Dictionary<string, string> ITtsTemplateService.GetDefaultMessages() => TtsDefaults.GetEnglishDefaults();

    /// <inheritdoc/>
    public Dictionary<string, string> GetDefaultsForLanguage(string languageCode) => TtsDefaults.GetDefaults(languageCode);

    private TtsTemplateDto MapToDto(TtsMessageTemplate entity)
    {
        Dictionary<string, string> messages;
        try
        {
            messages = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MessagesJson) ?? new();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Spoken template {TemplateId} ({LanguageCode}) has unreadable messages; it is listed as empty "
                + "and a call in that language falls back to the built-in wording",
                entity.Id, entity.LanguageCode);
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
