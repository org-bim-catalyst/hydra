using AskLucy.Application.Agents.Queries.GetSystemAgents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/047-admin-system-agents — admin-only, read-only visibility into the platform's
/// system-provisioned agents (specs/045's <c>SystemAgentProvisioner</c>). The existing
/// <see cref="AgentsController"/> is deliberately untouched: its <c>GET /agents</c> stays scoped
/// to the caller's own agents (spec.md FR-048) and can never surface a system-owned agent, since
/// <c>ListAgentsQuery</c> filters by owner id and a system agent's owner is never a real user.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/agents")]
public sealed class AdminAgentsController(ISender mediator) : ControllerBase
{
    [HttpGet("system")]
    public async Task<ActionResult<IReadOnlyList<AdminSystemAgentDto>>> GetSystemAgents(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSystemAgentsQuery(), cancellationToken));
}
