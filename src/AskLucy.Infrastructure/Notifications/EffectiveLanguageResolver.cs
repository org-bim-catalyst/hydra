using AskLucy.Application.Localization;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Localization;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// FR-044, research R14: the language a notification renders in. While localization is off the answer is always English. Otherwise it is
/// the first <i>supported</i> candidate of: the request's explicit language, the user's own choice, then English. A language that is no
/// longer supported falls through to the next candidate, so removing Arabic sends an Arabic user back to English without touching their choice.
/// </summary>
public sealed class EffectiveLanguageResolver(ILocalizationSettingsProvider settings, IUserLanguageStore users) : IEffectiveLanguageResolver
{
    public const string DefaultLanguage = PlatformLanguages.English;

    public async Task<string> ResolveAsync(string? recipientUserId, string? explicitLanguage, CancellationToken cancellationToken)
    {
        var snapshot = await settings.GetAsync(cancellationToken);
        if (!snapshot.IsEnabled)
        {
            return DefaultLanguage;
        }

        if (snapshot.Supports(explicitLanguage))
        {
            return PlatformLanguages.Normalize(explicitLanguage)!;
        }

        if (recipientUserId is not null)
        {
            var preferred = await users.GetPreferredLanguageAsync(recipientUserId, cancellationToken);
            if (snapshot.Supports(preferred))
            {
                return PlatformLanguages.Normalize(preferred)!;
            }
        }

        return DefaultLanguage;
    }
}
