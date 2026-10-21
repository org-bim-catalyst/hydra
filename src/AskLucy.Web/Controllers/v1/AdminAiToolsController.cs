using AskLucy.Application.Agents.Queries.ListAgentTools;
using AskLucy.Web.Auth;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/083 — admin-only, read-only visibility into the tools the platform's agents can call:
/// the native tools and the currently-active MCP tools, as the agent runtime itself sees them.
///
/// View-only by design, like System agents: what a deployment can call is a fact about it, not a
/// setting an administrator edits here.
/// </summary>
[ApiController]
[LocalizedSurface]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/ai-tools")]
public sealed class AdminAiToolsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("admin.ai-tools.view")]
    public async Task<ActionResult<IReadOnlyList<AgentToolDto>>> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAgentToolsQuery(), cancellationToken));
}
