using System.Globalization;
using System.Resources;

namespace AskLucy.Application.Localization;

/// <summary>
/// Server-side text for the localized surfaces (research R15): Problem Details titles and fixed details. It reads
/// <c>Messages.resx</c> and its per-language siblings through the BCL <see cref="ResourceManager"/>, so there is no new package.
/// A key with no entry for the culture yields null and the caller keeps its English text.
/// </summary>
public static class LocalizedMessages
{
    private static readonly ResourceManager Manager = new("AskLucy.Application.Localization.Messages", typeof(LocalizedMessages).Assembly);

    /// <summary>The text for <paramref name="key"/> in <paramref name="culture"/>; null for English, an unknown key, or a culture with no catalog.</summary>
    public static string? Get(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (culture.TwoLetterISOLanguageName == "en" || culture.Equals(CultureInfo.InvariantCulture))
        {
            return null;
        }

        // Never fall back to the neutral (English) resource here: the neutral value is what the exception already carries.
        var text = Manager.GetString(key, culture);
        var neutral = Manager.GetString(key, CultureInfo.InvariantCulture);
        return text is null || string.Equals(text, neutral, StringComparison.Ordinal) ? null : text;
    }
}
