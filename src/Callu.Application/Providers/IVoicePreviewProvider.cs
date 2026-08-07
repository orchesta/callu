using Callu.Shared.Models.Communication;

namespace Callu.Application.Providers;

/// <summary>A voice provider that can render sample text without placing a call.</summary>
// Only a synthesizer we can reach on its own can do this. A cloud provider's TTS runs inside a live
// call and has no endpoint to ask for a sample, so this is deliberately not on every provider.
public interface IVoicePreviewProvider
{
    Task<TtsPreviewResult> PreviewAsync(TtsPreviewRequest request, CancellationToken cancellationToken);

    /// <summary>Renders what a real call would say for a language, through the path a real call takes.</summary>
    // Deliberately not a second rendering of the same text: it resolves the template and builds the
    // segments the way a page does, so a preview cannot agree with a call that would disagree.
    Task<TtsPreviewResult> PreviewTemplateAsync(string languageCode, CancellationToken cancellationToken);

    /// <summary>One rendered sample as a WAV stream, or null when nothing is stored under the key.</summary>
    Task<Stream?> PreviewAudioAsync(string key, CancellationToken cancellationToken);
}
