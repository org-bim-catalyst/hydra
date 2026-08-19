using AskLucy.Application.Panels;
using MediatR;

namespace AskLucy.Application.Panels.Commands.SaveUserPanelPreference;

/// <summary>contracts/panel-preferences-api.md `PUT /api/v1/panels/preferences`.</summary>
public sealed record SaveUserPanelPreferenceCommand(int OpacityPercent) : IRequest<UserPanelPreferenceDto>;
