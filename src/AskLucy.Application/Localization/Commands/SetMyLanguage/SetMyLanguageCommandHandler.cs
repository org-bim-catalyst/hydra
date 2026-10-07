using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Localization;
using MediatR;

namespace AskLucy.Application.Localization.Commands.SetMyLanguage;

public sealed class SetMyLanguageCommandHandler(
    ILocalizationSettingsProvider settings,
    IUserLanguageStore languages,
    IEffectiveLanguageResolver resolver,
    ICurrentUserAccessor currentUser) : IRequestHandler<SetMyLanguageCommand, MyLocalizationDto>
{
    public async Task<MyLocalizationDto> Handle(SetMyLanguageCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var snapshot = await settings.GetAsync(cancellationToken);

        if (!snapshot.IsEnabled)
        {
            throw new LocalizationRejectedException("Language choice isn't available: localization is turned off.");
        }

        var language = PlatformLanguages.Normalize(request.PreferredLanguage);
        if (language is null || !snapshot.Supports(language))
        {
            throw new LocalizationRejectedException($"'{request.PreferredLanguage}' isn't a supported language.");
        }

        if (!await languages.SetPreferredLanguageAsync(userId, language, cancellationToken))
        {
            throw new KeyNotFoundException("The account was not found.");
        }

        return await MyLocalizationBuilder.BuildAsync(userId, settings, languages, resolver, cancellationToken);
    }
}
