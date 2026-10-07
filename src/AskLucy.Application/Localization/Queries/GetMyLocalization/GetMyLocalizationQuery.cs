using MediatR;

namespace AskLucy.Application.Localization.Queries.GetMyLocalization;

/// <summary>contracts/notifications-api.md GET /users/me/localization: the state that drives the language switch and the frontend's localized surfaces.</summary>
public sealed record GetMyLocalizationQuery : IRequest<MyLocalizationDto>;
