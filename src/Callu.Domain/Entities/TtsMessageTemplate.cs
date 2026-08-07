using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>Per-language TTS message templates for VoxEngine incident calls.</summary>
public class TtsMessageTemplate : BaseEntity
{
    /// <summary>
    /// Language code (e.g., "tr-TR", "en-US", "de-DE")
    /// </summary>
    [Required]
    [StringLength(10)]
    public string LanguageCode { get; set; } = "en-US";

    /// <summary>
    /// Human-readable language name (e.g., "Türkçe", "English", "Deutsch")
    /// </summary>
    [Required]
    [StringLength(50)]
    public string DisplayName { get; set; } = "English";

    /// <summary>
    /// Whether this is the default language used when no specific language is configured
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>JSON dictionary of message key to template text.</summary>
    [Required]
    public string MessagesJson { get; set; } = "{}";
}
