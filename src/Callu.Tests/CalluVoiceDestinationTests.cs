using System.Text.RegularExpressions;
using Callu.Infrastructure.Providers.CalluVoice;

namespace Callu.Tests;

/// <summary>
/// What the adapter does with a stored phone number. callu-voice dials E.164 only and answers a
/// malformed number with a 400 and no callback, so a number that cannot be put in that shape has to
/// be refused here, where the refusal can say what to fix.
/// </summary>
public class CalluVoiceDestinationTests
{
    /// <summary>callu-voice's own destination rule, so these tests fail if the two ever drift apart.</summary>
    private static readonly Regex CalluVoiceAccepts = new(@"^\+[1-9]\d{6,14}$");

    [Theory]
    // Already in the shape callu-voice wants.
    [InlineData("+905321234567", "+905321234567")]
    // Formatting an operator typed, in every separator a phone number is usually written with.
    [InlineData("+90 532 123 45 67", "+905321234567")]
    [InlineData("+90-532-123-45-67", "+905321234567")]
    [InlineData("(+90) 532/123.45.67", "+905321234567")]
    [InlineData("  +905321234567  ", "+905321234567")]
    // A non-breaking space, which is what a copy-paste out of a browser leaves behind.
    [InlineData("+90 532 123 45 67", "+905321234567")]
    // "00" is the international access code: replacing it with "+" states nothing the number did not.
    [InlineData("00905321234567", "+905321234567")]
    [InlineData("00 90 532 123 45 67", "+905321234567")]
    // The shortest and longest E.164 numbers there are.
    [InlineData("+1234567", "+1234567")]
    [InlineData("+123456789012345", "+123456789012345")]
    public void AnUnambiguousNumberIsNormalized(string stored, string dialled)
    {
        var result = CalluVoiceDestination.Normalize(stored);

        Assert.True(result.IsUsable, result.Refusal);
        Assert.Equal(dialled, result.Number);
        Assert.Matches(CalluVoiceAccepts, result.Number!);
    }

    [Theory]
    // A national number. Turning this into E.164 means inventing the country it belongs to.
    [InlineData("0532 123 45 67")]
    [InlineData("05321234567")]
    // Bare digits. "905321234567" looks obvious and "+5321234567" is a real number in another
    // country, and nothing in the digits themselves says which of the two this is.
    [InlineData("905321234567")]
    [InlineData("5321234567")]
    // The NANP international prefix. "011" is also a national area code elsewhere.
    [InlineData("011905321234567")]
    // No country code starts with zero.
    [InlineData("+0532123456")]
    [InlineData("+00905321234567")]
    // Not a number at all, or not only a number.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+90532abc4567")]
    [InlineData("+905321234567 ext 12")]
    [InlineData("call me")]
    // Outside the 7-to-15-digit range E.164 allows.
    [InlineData("+90532")]
    [InlineData("+9053212345678901")]
    public void AnAmbiguousNumberIsRefusedRatherThanGuessedAt(string stored)
    {
        var result = CalluVoiceDestination.Normalize(stored);

        Assert.False(result.IsUsable);
        Assert.Null(result.Number);
        Assert.NotNull(result.Refusal);
    }

    /// <summary>A refusal an operator cannot act on is the same as no page at all, twice over.</summary>
    [Fact]
    public void TheRefusalNamesTheNumber_AndSaysWhatToDoAboutIt()
    {
        var refusal = CalluVoiceDestination.Normalize("0532 123 45 67").Refusal;

        Assert.NotNull(refusal);
        Assert.Contains("0532 123 45 67", refusal!);
        Assert.Contains("E.164", refusal!);
        Assert.Contains("+905321234567", refusal!);
        Assert.Contains("profile", refusal!, StringComparison.OrdinalIgnoreCase);
        // The operator has to know the page did not go out; "refused" alone reads like a retry.
        Assert.Contains("nothing was dialled", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one rule normalization must never break: a number that came in without a country code
    /// must not leave with one, whatever it looks like.
    /// </summary>
    [Theory]
    [InlineData("5321234567")]
    [InlineData("905321234567")]
    [InlineData("0532 123 45 67")]
    [InlineData("532-123-45-67")]
    public void NoCountryCodeIsEverInvented(string stored) =>
        Assert.Null(CalluVoiceDestination.Normalize(stored).Number);

    /// <summary>Every number this adapter agrees to dial is one callu-voice's own validator accepts.</summary>
    [Theory]
    [InlineData("+905321234567")]
    [InlineData("00905321234567")]
    [InlineData("+1 (415) 555-0132")]
    [InlineData("+49 30 901820")]
    public void WhatIsNormalizedIsWhatCalluVoiceWouldAccept(string stored)
    {
        var result = CalluVoiceDestination.Normalize(stored);

        Assert.True(result.IsUsable, result.Refusal);
        Assert.Matches(CalluVoiceAccepts, result.Number!);
    }
}
