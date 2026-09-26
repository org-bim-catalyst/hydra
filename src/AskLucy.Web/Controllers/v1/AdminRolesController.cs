using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;
using AskLucy.Application.Authorization.Roles.Commands.CreateRole;
using AskLucy.Application.Authorization.Roles.Commands.DeleteRole;
using AskLucy.Application.Authorization.Roles.Commands.SetAdministratorContentAccess;
using AskLucy.Application.Authorization.Roles.Commands.UpdateRole;
using AskLucy.Application.Authorization.Roles.Queries.GetAdministratorContentAccess;
using AskLucy.Application.Authorization.Roles.Queries.GetRole;
using AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;
using AskLucy.Application.Authorization.Roles.Queries.ListRoles;
using AskLucy.Application.Users;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Role definition (specs/055-role-management User Story 1). Reserved to the built-in
/// Administrator/Super User roles — never delegable through a custom role's own permissions
/// (FR-002) — so this stays on <c>AdministratorOrSuperUser</c> rather than a
/// <c>[RequirePermission]</c> attribute.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/roles")]
public sealed class AdminRolesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<RoleSummaryDto>>> GetRoles(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListRolesQuery(search, page, pageSize), cancellationToken));

    [HttpGet("{roleId}")]
    public async Task<ActionResult<RoleSummaryDto>> GetRole(string roleId, CancellationToken cancellationToken)
    {
        var role = await mediator.Send(new GetRoleQuery(roleId), cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    public async Task<ActionResult<RoleSummaryDto>> CreateRole(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var created = await mediator.Send(new CreateRoleCommand(request.Name, request.Description, request.PermissionKeys), cancellationToken);
        return CreatedAtAction(nameof(GetRole), new { roleId = created.Id }, created);
    }

    [HttpPut("{roleId}")]
    public async Task<ActionResult<RoleSummaryDto>> UpdateRole(string roleId, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdateRoleCommand(roleId, request.Name, request.Description, request.PermissionKeys, request.ConcurrencyStamp), cancellationToken));

    [HttpDelete("{roleId}")]
    public async Task<ActionResult<DeleteRoleResult>> DeleteRole(string roleId, [FromQuery] string concurrencyStamp, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DeleteRoleCommand(roleId, concurrencyStamp), cancellationToken));

    /// <summary>specs/074 FR-016g — whether the built-in Administrator role may view user content.</summary>
    [HttpGet("administrator/content-access")]
    public async Task<ActionResult<AdministratorContentAccessDto>> GetAdministratorContentAccess(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdministratorContentAccessQuery(), cancellationToken));

    /// <summary>specs/074 FR-016g — Super User only; the handler refuses everyone else with a 403.</summary>
    [HttpPut("administrator/content-access")]
    public async Task<ActionResult<AdministratorContentAccessDto>> SetAdministratorContentAccess(
        SetAdministratorContentAccessRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetAdministratorContentAccessCommand(request.Granted), cancellationToken));

    /// <summary>specs/056-bulk-select-all — resolves the "all matching" id set server-side at execution time.</summary>
    [HttpGet("actions/bulk-eligible-ids")]
    public async Task<ActionResult<BulkEligibleIdsResponse>> GetBulkEligibleIds(
        [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(new BulkEligibleIdsResponse(await mediator.Send(new GetRolesEligibleIdsQuery(search), cancellationToken)));

    [HttpPost("actions/bulk-delete")]
    public async Task<ActionResult<BulkDeleteRolesResultResponse>> BulkDelete(BulkTargetRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new BulkDeleteRolesCommand(new Application.Common.BulkTarget(request.Ids, request.AllMatching), request.Search),
            cancellationToken);

        return Ok(new BulkDeleteRolesResultResponse(
            result.Outcome.SucceededCount,
            result.Outcome.Skipped.Select(s => new BulkActionSkipResponse(s.Id, s.Reason)).ToList(),
            result.UnassignedUserCounts));
    }
}
