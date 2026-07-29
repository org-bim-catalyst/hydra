using AskLucy.Application.Admin;
using AskLucy.Application.Admin.Queries.GetDashboardSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Admin Dashboard (specs/001-admin-dashboard FR-001) — role-gated server-side, matching <see cref="UsersController"/>'s admin routes.</summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/dashboard")]
public sealed class AdminDashboardController(ISender mediator) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminDashboardSummaryQuery(), cancellationToken));
}
