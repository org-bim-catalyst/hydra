using AskLucy.Application.Authorization.Assignments.Commands.AssignRole;
using AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;
using AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;
using AskLucy.Application.Authorization.Assignments.Queries.ListRoleAssignments;
using AskLucy.Application.Users;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Role assignment (specs/055-role-management User Story 2). Reserved to the built-in
/// Administrator/Super User roles (FR-002) — same rationale as <see cref="AdminRolesController"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/role-assignments")]
public sealed class AdminRoleAssignmentsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<RoleAssignmentDto>>> GetAssignments(
        [FromQuery] string? search, [FromQuery] string? roleId, [FromQuery] bool assignableOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListRoleAssignmentsQuery(search, roleId, assignableOnly, page, pageSize), cancellationToken));

    [HttpPut("{userId}")]
    public async Task<IActionResult> AssignRole(string userId, AssignRoleRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new AssignRoleCommand(userId, request.RoleId, request.ExpectedCurrentRoleId), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/056-bulk-select-all — resolves the "all matching" id set server-side at execution time.</summary>
    [HttpGet("actions/bulk-eligible-ids")]
    public async Task<ActionResult<BulkEligibleIdsResponse>> GetBulkEligibleIds(
        [FromQuery] string roleId, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(new BulkEligibleIdsResponse(await mediator.Send(new GetRoleAssignmentsEligibleIdsQuery(roleId, search), cancellationToken)));

    [HttpPost("actions/bulk-assign")]
    public async Task<ActionResult<BulkActionResultResponse>> BulkAssign(BulkAssignRoleRequest request, CancellationToken cancellationToken)
    {
        var outcome = await mediator.Send(
            new BulkAssignRoleCommand(request.RoleId, new Application.Common.BulkTarget(request.Ids, request.AllMatching), request.Search),
            cancellationToken);

        return Ok(new BulkActionResultResponse(
            outcome.SucceededCount, outcome.Skipped.Select(s => new BulkActionSkipResponse(s.Id, s.Reason)).ToList()));
    }
}
