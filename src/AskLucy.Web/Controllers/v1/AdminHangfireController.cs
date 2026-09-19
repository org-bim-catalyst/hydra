using AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;
using AskLucy.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Mints the short-lived session that lets an already-authenticated admin's next browser
/// navigation to <c>/hangfire</c> authenticate (specs/060-hangfire-dashboard-access). Reserved
/// to the built-in Administrator/Super User roles — same rationale, and the same policy, as
/// <see cref="AdminRoleAssignmentsController"/> and <see cref="HangfireDashboardAuthorizationFilter"/>
/// itself, which re-checks role membership independently rather than trusting this endpoint's
/// gate alone.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/hangfire")]
public sealed class AdminHangfireController(ISender mediator) : ControllerBase
{
    [HttpPost("session")]
    public async Task<IActionResult> IssueSession(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new IssueHangfireDashboardSessionCommand(), cancellationToken);

        Response.Cookies.Append(
            HangfireDashboardCookie.Name,
            result.AccessToken,
            HangfireDashboardCookie.BuildOptions(result.Lifetime));

        return NoContent();
    }
}
