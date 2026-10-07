using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using MediatR;

namespace AskLucy.Application.Localization.Queries.GetMyLocalization;

public sealed class GetMyLocalizationQueryHandler(
    ILocalizationSettingsProvider settings,
    IUserLanguageStore languages,
    IEffectiveLanguageResolver resolver,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMyLocalizationQuery, MyLocalizationDto>
{
    public async Task<MyLocalizationDto> Handle(GetMyLocalizationQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await MyLocalizationBuilder.BuildAsync(userId, settings, languages, resolver, cancellationToken);
    }
}
