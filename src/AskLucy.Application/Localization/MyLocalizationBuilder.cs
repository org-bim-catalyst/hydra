using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Localization;

namespace AskLucy.Application.Localization;

/// <summary>Builds the caller's language state; the same shape answers the GET and the PUT.</summary>
internal static class MyLocalizationBuilder
{
    public static async Task<MyLocalizationDto> BuildAsync(
        string userId,
        ILocalizationSettingsProvider settings,
        IUserLanguageStore languages,
        IEffectiveLanguageResolver resolver,
        CancellationToken cancellationToken)
    {
        var snapshot = await settings.GetAsync(cancellationToken);
        var preferred = await languages.GetPreferredLanguageAsync(userId, cancellationToken);
        var effective = await resolver.ResolveAsync(userId, null, cancellationToken);

        return new MyLocalizationDto(
            snapshot.IsEnabled,
            [.. snapshot.SelectableLanguages.Select(code => PlatformLanguages.Find(code)!).Select(l => new LanguageOptionDto(l.Code, l.NativeName))],
            preferred,
            effective,
            PlatformLanguages.Find(effective)?.Direction ?? "ltr");
    }
}
