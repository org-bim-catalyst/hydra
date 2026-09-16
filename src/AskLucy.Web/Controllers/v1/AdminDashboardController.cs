using AskLucy.Application.Admin;
using AskLucy.Application.Admin.Queries.GetDashboardSummary;
using AskLucy.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Admin Dashboard (specs/001-admin-dashboard FR-001) — permission-gated server-side (specs/055-role-management research.md Decision 5).</summary>
[ApiController]
[RequirePermission("admin.dashboard.view")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/dashboard")]
public sealed class AdminDashboardController(ISender mediator) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminDashboardSummaryQuery(), cancellationToken));
}
