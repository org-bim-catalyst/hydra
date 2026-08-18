using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.ArchiveAgent;
using AskLucy.Application.Agents.Commands.CreateAgent;
using AskLucy.Application.Agents.Commands.DeleteAgent;
using AskLucy.Application.Agents.Commands.DuplicateAgent;
using AskLucy.Application.Agents.Commands.PublishAgentVersion;
using AskLucy.Application.Agents.Commands.RestoreAgent;
using AskLucy.Application.Agents.Commands.UpdateAgent;
using AskLucy.Application.Agents.Queries.GetAgent;
using AskLucy.Application.Agents.Queries.GetAgentVersion;
using AskLucy.Application.Agents.Queries.ListAgents;
using AskLucy.Application.Agents.Queries.ListAgentVersions;
using AskLucy.Application.Common;
using AskLucy.Domain.Agents;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Agent definition/lifecycle/versioning (contracts/agents-api.md). Every operation is implicitly scoped to the caller (spec.md FR-048).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("agent-endpoints")]
[Route("api/v1/agents")]
public sealed class AgentsController(ISender mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AgentDetailDto>> Create([FromBody] CreateAgentRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateAgentCommand(
                request.Name, request.Description, request.AgentType, request.Instructions,
                request.ModelProviderId, request.ModelId, request.OutputFormat, request.ExecutionPolicy),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentQuery(id), cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<AgentListItemDto>>> List(
        [FromQuery] AgentStatus? status = null,
        [FromQuery] AgentType? agentType = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListAgentsQuery(status, agentType, cursor, pageSize), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AgentDetailDto>> Update(Guid id, [FromBody] UpdateAgentRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdateAgentCommand(
                id, request.Name, request.Description, request.AgentType, request.Instructions,
                request.ModelProviderId, request.ModelId, request.OutputFormat, request.ExecutionPolicy,
                request.Tools, request.KnowledgeBaseIds, request.MemoryPolicy),
            cancellationToken));

    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<AgentVersionDto>> Publish(Guid id, [FromBody] PublishAgentVersionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PublishAgentVersionCommand(id, request.ChangeDescription), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<AgentVersionDto>>> ListVersions(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAgentVersionsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    public async Task<ActionResult<AgentVersionDto>> GetVersion(Guid id, int versionNumber, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentVersionQuery(id, versionNumber), cancellationToken));

    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<AgentDetailDto>> Duplicate(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DuplicateAgentCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<ActionResult<AgentDetailDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ArchiveAgentCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/restore")]
    public async Task<ActionResult<AgentDetailDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreAgentCommand(id), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteAgentCommand(id), cancellationToken);
        return NoContent();
    }
}
