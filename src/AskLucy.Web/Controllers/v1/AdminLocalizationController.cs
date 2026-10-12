using AskLucy.Application.Localization;
using AskLucy.Application.Localization.Commands.UpdateLocalizationSettings;
using AskLucy.Application.Localization.Queries.GetLocalizationSettings;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// The platform localization switch (specs/067 US8, contracts/admin-notifications-api.md). Reading needs <c>admin.notifications.view</c> and
/// changing it needs <c>admin.notifications.manage</c> plus <c>If-Match</c>, the base64 row version as last read.
/// </summary>
[ApiController]
[LocalizedSurface]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/notifications/localization")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class AdminLocalizationController(ISender mediator) : ControllerBase
{
    [ProducesResponseType<AdminLocalizationDto>(StatusCodes.Status200OK)]
    [HttpGet]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<AdminLocalizationDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetLocalizationSettingsQuery(), cancellationToken));

    [ProducesResponseType<AdminLocalizationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    [HttpPut]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<AdminLocalizationDto>> Update([FromBody] UpdateLocalizationRequest request, CancellationToken cancellationToken)
    {
        var header = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(header);
        }
        catch (FormatException)
        {
            rowVersion = [];
        }

        if (rowVersion.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "If-Match required",
                detail: "Send the settings' rowVersion in the If-Match header.");
        }

        return Ok(await mediator.Send(
            new UpdateLocalizationSettingsCommand(request.IsEnabled, request.SupportedLanguages ?? [], rowVersion), cancellationToken));
    }
}
