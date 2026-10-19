using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.UpdateProviderHealthRetention;
using AskLucy.Application.Ai.Queries.GetProviderHealthRetention;
using AskLucy.Domain.Authorization;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/082 — the provider health-check retention window. Reading needs <c>admin.operational-failures.view</c> and
/// changing it needs <c>admin.operational-failures.manage</c> plus <c>If-Match</c>, the base64 row version as last read.
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/provider-health/retention")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class AdminProviderHealthRetentionController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<ProviderHealthRetentionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProviderHealthRetentionDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetProviderHealthRetentionQuery(), cancellationToken));

    [HttpPut]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<ProviderHealthRetentionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ProviderHealthRetentionDto>> Update(
        [FromBody] UpdateProviderHealthRetentionRequest request,
        CancellationToken cancellationToken)
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
            new UpdateProviderHealthRetentionCommand(request.RetentionDays, rowVersion), cancellationToken));
    }
}
