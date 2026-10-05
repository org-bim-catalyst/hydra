using AskLucy.Application.Appearance;
using AskLucy.Application.Appearance.Commands.UpdatePresenceSphereSettings;
using AskLucy.Application.Appearance.Queries.GetPresenceSphereSettings;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/080 contracts/presence-sphere-api.md — the workspace-wide look of the presence sphere. Every signed-in
/// user reads it (their chat applies it); only an administrator holding "Manage appearance" changes it.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/appearance/presence-sphere")]
public sealed class AppearanceController(ISender mediator) : ControllerBase
{
    /// <summary>The saved settings, or the defaults while none have been saved.</summary>
    [HttpGet]
    public async Task<ActionResult<PresenceSphereSettingsDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetPresenceSphereSettingsQuery(), cancellationToken));

    /// <summary>Replaces all three settings. An out-of-range value is a 400 naming the allowed range; nothing is saved.</summary>
    [HttpPut]
    [RequirePermission("admin.appearance.manage")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PresenceSphereSettingsDto>> Update(UpdatePresenceSphereSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdatePresenceSphereSettingsCommand(request.DotSizeMultiplier, request.CardFillPercent, request.ZoomEnabled),
            cancellationToken));
}
