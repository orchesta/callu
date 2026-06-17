using Callu.Shared.Models.Communication;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing per-language TTS message templates.
/// Templates are used by VoxEngine scripts for localized voice prompts.
/// </summary>
public interface ITtsTemplateService
{
    /// <summary>
    /// Get all configured language templates
    /// </summary>
    Task<List<TtsTemplateDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a single language's template
    /// </summary>
    Task<TtsTemplateDto?> GetByLanguageAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Create or update a language's message templates
    /// </summary>
    Task SaveAsync(TtsTemplateSaveRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete a language's templates
    /// </summary>
    Task DeleteAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Resolve messages for a given language code.
    /// Returns merged dict: language-specific messages + English fallback defaults.
    /// </summary>
    Task<Dictionary<string, string>> ResolveMessagesAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Get the built-in default message keys and their English values.
    /// </summary>
    Dictionary<string, string> GetDefaultMessages();

    /// <summary>
    /// Get the built-in default messages for a specific language.
    /// Falls back to English if no defaults exist for the language.
    /// </summary>
    Dictionary<string, string> GetDefaultsForLanguage(string languageCode);
}
