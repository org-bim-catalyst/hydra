using AskLucy.Application.Agents.Commands.UpdateToolSettings;
using AskLucy.Application.Agents.Queries.GetToolSettings;
using AskLucy.Application.Agents.Queries.ListAgentTools;
using AskLucy.Web.Auth;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/083 — admin-only visibility into the tools the platform's agents can call: the native
/// tools and the currently-active MCP tools, as the agent runtime itself sees them.
/// <para>
/// Reading the catalogue is a fact about the deployment, so it is view-only. Changing the
/// defaults a tool runs with is a configuration decision, and that is what the manage permission
/// gates: the map snapshot's default imagery provider, how much it crops, and how far it zooms.
/// </para>
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

    /// <summary>
    /// The settings every configurable tool declares, with the values that apply now. A tool that
    /// declares none is absent, which is what leaves its gear disabled.
    /// </summary>
    [HttpGet("settings")]
    [RequirePermission("admin.ai-tools.view")]
    public async Task<ActionResult<IReadOnlyList<ToolSettingsDto>>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetToolSettingsQuery(), cancellationToken));

    /// <summary>Saves some or all of one tool's settings. Keys not named keep their current value.</summary>
    [HttpPut("{tool}/settings")]
    [RequirePermission("admin.ai-tools.manage")]
    public async Task<IActionResult> UpdateSettings(
        string tool, UpdateToolSettingsRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateToolSettingsCommand(tool, request.Values), cancellationToken);
        return NoContent();
    }
}

/// <summary>The settings one tool declares, edited from the admin AI Tools page.</summary>
public sealed record UpdateToolSettingsRequest(IReadOnlyDictionary<string, string> Values);
