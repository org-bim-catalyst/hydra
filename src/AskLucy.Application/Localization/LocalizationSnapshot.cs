using AskLucy.Domain.Localization;

namespace AskLucy.Application.Localization;

/// <summary>The platform's localization state at one moment, as the cached provider hands it out.</summary>
public sealed record LocalizationSnapshot(bool IsEnabled, IReadOnlyList<string> SupportedLanguages)
{
    /// <summary>The state while localization is off (and before the row exists): English only.</summary>
    public static LocalizationSnapshot EnglishOnly { get; } = new(false, [PlatformLanguages.English]);

    /// <summary>The languages a user may choose from: just English while localization is off.</summary>
    public IReadOnlyList<string> SelectableLanguages => IsEnabled ? SupportedLanguages : [PlatformLanguages.English];

    /// <summary>Whether content in <paramref name="code"/> may be shown: English always, any other language only while localization is on and lists it.</summary>
    public bool Supports(string? code)
    {
        var normalized = PlatformLanguages.Normalize(code);
        return normalized is not null && SelectableLanguages.Contains(normalized, StringComparer.Ordinal);
    }
}
