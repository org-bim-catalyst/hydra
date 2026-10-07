using MediatR;

namespace AskLucy.Application.Localization.Queries.GetLocalizationSettings;

/// <summary>contracts/admin-notifications-api.md GET /localization (V).</summary>
public sealed record GetLocalizationSettingsQuery : IRequest<AdminLocalizationDto>;
