namespace AskLucy.Application.Localization;

public sealed record LanguageOptionDto(string Code, string NativeName);

/// <summary>contracts/notifications-api.md GET /users/me/localization.</summary>
public sealed record MyLocalizationDto(
    bool LocalizationEnabled,
    IReadOnlyList<LanguageOptionDto> SupportedLanguages,
    string? PreferredLanguage,
    string EffectiveLanguage,
    string Direction);

public sealed record AvailableLanguageDto(string Code, string NativeName, bool Locked);

/// <summary>contracts/admin-notifications-api.md GET /localization.</summary>
public sealed record AdminLocalizationDto(
    bool IsEnabled,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<AvailableLanguageDto> AvailableLanguages,
    string RowVersion);
