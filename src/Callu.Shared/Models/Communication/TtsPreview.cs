using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Callu.Shared.Models.Communication;

/// <summary>Bounds a preview request is held to, at or below what the voice service accepts.</summary>
// Kept below the far side's own limits so an over-long request is a field error here rather than a
// refusal from the synthesizer, and so a preview cannot be used to spend its CPU without bound.
public static class TtsPreviewLimits
{
    public const int MaxSegments = 16;
    public const int MaxSegmentChars = 1000;
    public const int MaxLanguageChars = 16;
    public const int MaxVoiceChars = 8;
}

/// <summary>One piece of text to hear, and the language to hear it in.</summary>
public sealed class TtsPreviewSegmentRequest
{
    [StringLength(TtsPreviewLimits.MaxSegmentChars)]
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [StringLength(TtsPreviewLimits.MaxLanguageChars)]
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;
}

/// <summary>What the operator wants rendered. Segments carry their own language, as a real call does.</summary>
public sealed class TtsPreviewRequest
{
    public List<TtsPreviewSegmentRequest> Segments { get; set; } = [];

    [StringLength(TtsPreviewLimits.MaxVoiceChars)]
    public string? Voice { get; set; }
}

/// <summary>What one segment became.</summary>
// Normalized is the text the synthesizer actually spoke; a number left unread or a language that
// never applied shows up there without anyone having to listen.
public sealed class TtsPreviewSegment
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("normalized")]
    public string Normalized { get; set; } = string.Empty;

    [JsonPropertyName("duration_s")]
    public double DurationSeconds { get; set; }
}

/// <summary>The rendered segments, or why nothing could be rendered.</summary>
public sealed record TtsPreviewResult(
    bool Success,
    IReadOnlyList<TtsPreviewSegment> Segments,
    string? ErrorMessage);
