using MediatR;

namespace AskLucy.Application.Localization.Commands.SetMyLanguage;

/// <summary>contracts/notifications-api.md PUT /users/me/localization. 422 when localization is off or the language isn't supported.</summary>
public sealed record SetMyLanguageCommand(string PreferredLanguage) : IRequest<MyLocalizationDto>;
