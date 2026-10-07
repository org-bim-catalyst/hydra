using AskLucy.Domain.Localization;

namespace AskLucy.Application.Localization;

internal static class LocalizationMapper
{
    public static AdminLocalizationDto ToAdminDto(LocalizationSetting setting) => new(
        setting.IsEnabled,
        setting.SupportedLanguages,
        [.. PlatformLanguages.All.Select(l => new AvailableLanguageDto(l.Code, l.NativeName, Locked: l.Code == PlatformLanguages.English))],
        Convert.ToBase64String(setting.RowVersion));
}
