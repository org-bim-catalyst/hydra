using AskLucy.Application.Panels;
using MediatR;

namespace AskLucy.Application.Panels.Queries.GetUserPanelPreference;

/// <summary>contracts/panel-preferences-api.md `GET /api/v1/panels/preferences`.</summary>
public sealed record GetUserPanelPreferenceQuery : IRequest<UserPanelPreferenceDto>;
