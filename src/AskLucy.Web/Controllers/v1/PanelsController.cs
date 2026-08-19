using AskLucy.Application.Panels;
using AskLucy.Application.Panels.Commands.SaveUserPanelPreference;
using AskLucy.Application.Panels.Queries.GetUserPanelPreference;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// The floating panel opacity preference (specs/028-ai-floating-panels FR-011/FR-012,
/// contracts/panel-preferences-api.md).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/panels")]
[EnableRateLimiting("panels-endpoints")]
public sealed class PanelsController(ISender mediator) : ControllerBase
{
    [HttpGet("preferences")]
    public async Task<ActionResult<UserPanelPreferenceDto>> GetPreferences(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetUserPanelPreferenceQuery(), cancellationToken));

    [HttpPut("preferences")]
    public async Task<ActionResult<UserPanelPreferenceDto>> SavePreferences(
        SavePanelPreferencesRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SaveUserPanelPreferenceCommand(request.OpacityPercent), cancellationToken));
}
