namespace Callu.Shared.Models.Communication;

/// <summary>The messages a call will speak, and the language they are actually in.</summary>
// Resolution falls back to another language's template when the requested one has none, so the
// synthesizer has to be told the language of the text it was handed, not the one that was asked for.
public sealed record TtsResolvedMessages(string LanguageCode, Dictionary<string, string> Messages);
