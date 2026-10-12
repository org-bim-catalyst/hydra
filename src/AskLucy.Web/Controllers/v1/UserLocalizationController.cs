using AskLucy.Application.Localization;
using AskLucy.Application.Localization.Commands.SetMyLanguage;
using AskLucy.Application.Localization.Queries.GetMyLocalization;
using AskLucy.Web.Contracts;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>The caller's language state and choice (specs/067 US8, contracts/notifications-api.md). Always the caller's own.</summary>
[ApiController]
[LocalizedSurface]
[Authorize]
[EnableRateLimiting("notifications-endpoints")]
[Route("api/v1/users/me/localization")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class UserLocalizationController(ISender mediator) : ControllerBase
{
    [ProducesResponseType<MyLocalizationDto>(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<ActionResult<MyLocalizationDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMyLocalizationQuery(), cancellationToken));

    [ProducesResponseType<MyLocalizationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [HttpPut]
    public async Task<ActionResult<MyLocalizationDto>> Set([FromBody] SetMyLanguageRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetMyLanguageCommand(request.PreferredLanguage ?? string.Empty), cancellationToken));
}
