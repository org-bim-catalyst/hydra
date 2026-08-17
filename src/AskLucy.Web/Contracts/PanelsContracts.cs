namespace AskLucy.Web.Contracts;

/// <summary>contracts/panel-preferences-api.md `PUT /api/v1/panels/preferences`.</summary>
public sealed record SavePanelPreferencesRequest(int OpacityPercent);
