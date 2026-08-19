using AskLucy.Application.Mcp;
using AskLucy.Application.Mcp.Commands.DuplicateMcpPrompt;
using AskLucy.Application.Mcp.Queries.GetMcpTool;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpPrompts;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpResources;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpTools;
using AskLucy.Application.Prompts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>MCP tool/resource/prompt catalog browsing (contracts/mcp-api.md) — any authenticated user, no admin gate; exactly the set an agent could actually call (FR-062).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("mcp-endpoints")]
[Route("api/v1/mcp/catalog")]
public sealed class McpCatalogController(ISender mediator) : ControllerBase
{
    [HttpGet("tools")]
    public async Task<ActionResult<IReadOnlyList<McpToolCatalogSummaryDto>>> ListTools(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAvailableMcpToolsQuery(), cancellationToken));

    [HttpGet("tools/{namespacedName}")]
    public async Task<ActionResult<McpToolDetailDto>> GetTool(string namespacedName, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMcpToolQuery(namespacedName), cancellationToken));

    [HttpGet("resources")]
    public async Task<ActionResult<IReadOnlyList<McpResourceCatalogSummaryDto>>> ListResources(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAvailableMcpResourcesQuery(), cancellationToken));

    [HttpGet("prompts")]
    public async Task<ActionResult<IReadOnlyList<McpPromptCatalogSummaryDto>>> ListPrompts(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAvailableMcpPromptsQuery(), cancellationToken));

    [HttpPost("prompts/{namespacedName}/actions/duplicate")]
    public async Task<ActionResult<PromptDetailDto>> DuplicatePrompt(string namespacedName, CancellationToken cancellationToken)
    {
        var duplicate = await mediator.Send(new DuplicateMcpPromptCommand(namespacedName), cancellationToken);
        return CreatedAtAction("Get", "Prompts", new { id = duplicate.Id }, duplicate);
    }
}
