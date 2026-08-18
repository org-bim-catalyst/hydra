using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.ApproveAgentAction;
using AskLucy.Application.Agents.Commands.CancelAgentExecution;
using AskLucy.Application.Agents.Commands.PauseAgentExecution;
using AskLucy.Application.Agents.Commands.RejectAgentAction;
using AskLucy.Application.Agents.Commands.ResumeAgentExecution;
using AskLucy.Application.Agents.Commands.StartAgentExecution;
using AskLucy.Application.Agents.Queries.GetAgentApproval;
using AskLucy.Application.Agents.Queries.GetAgentExecution;
using AskLucy.Application.Agents.Queries.GetAgentExecutionEvents;
using AskLucy.Application.Agents.Queries.GetAgentExecutionSteps;
using AskLucy.Application.Agents.Queries.GetAgentExecutionUsage;
using AskLucy.Application.Agents.Queries.GetAgentToolCalls;
using AskLucy.Application.Agents.Queries.ListAgentExecutions;
using AskLucy.Application.Common;
using AskLucy.Domain.Agents;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Agent execution runtime (contracts/agents-api.md). Every operation is implicitly scoped to the caller (spec.md FR-048/SC-010).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("agent-endpoints")]
[Route("api/v1/agent-executions")]
public sealed class AgentExecutionsController(ISender mediator) : ControllerBase
{
    /// <summary>Never finishes synchronously (spec.md FR-017) — the run continues in the background; poll <see cref="Get"/> or subscribe to the execution hub for progress.</summary>
    [HttpPost]
    public async Task<ActionResult<AgentExecutionSummaryDto>> Start([FromBody] StartAgentExecutionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new StartAgentExecutionCommand(
                request.AgentId, request.AgentVersionNumber, request.Objective,
                request.ConversationIntegrationMode, request.UserChatId, request.IsTestExecution),
            cancellationToken);

        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentExecutionDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentExecutionQuery(id), cancellationToken));

    /// <summary>User Story 5 execution history — cursor-paginated (contracts/agents-api.md).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AgentExecutionSummaryDto>>> List(
        [FromQuery] Guid? agentId, [FromQuery] AgentExecutionStatus? status, [FromQuery] bool? isTestExecution,
        [FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListAgentExecutionsQuery(agentId, status, isTestExecution, cursor, pageSize), cancellationToken));

    [HttpGet("{id:guid}/steps")]
    public async Task<ActionResult<IReadOnlyList<AgentExecutionStepDto>>> GetSteps(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentExecutionStepsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/tool-calls")]
    public async Task<ActionResult<IReadOnlyList<AgentToolCallDto>>> GetToolCalls(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentToolCallsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<AgentExecutionUsageDto>> GetUsage(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentExecutionUsageQuery(id), cancellationToken));

    [HttpGet("{id:guid}/approvals/{approvalId:guid}")]
    public async Task<ActionResult<AgentApprovalDto>> GetApproval(Guid id, Guid approvalId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentApprovalQuery(id, approvalId), cancellationToken));

    /// <summary>Resumes the paused execution in the background — never finishes synchronously (spec.md FR-017/FR-025).</summary>
    [HttpPost("{id:guid}/approvals/{approvalId:guid}/approve")]
    public async Task<ActionResult<AgentApprovalDto>> ApproveApproval(Guid id, Guid approvalId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ApproveAgentActionCommand(id, approvalId), cancellationToken));

    [HttpPost("{id:guid}/approvals/{approvalId:guid}/reject")]
    public async Task<ActionResult<AgentApprovalDto>> RejectApproval(Guid id, Guid approvalId, [FromBody] RejectAgentActionRequest? request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RejectAgentActionCommand(id, approvalId, request?.Reason), cancellationToken));

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<AgentExecutionEventDto>>> GetEvents(Guid id, [FromQuery] DateTime? since, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAgentExecutionEventsQuery(id, since), cancellationToken));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new PauseAgentExecutionCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Resumes a user-paused execution in the background — never finishes synchronously (spec.md FR-017).</summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResumeAgentExecutionCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new CancelAgentExecutionCommand(id), cancellationToken);
        return NoContent();
    }
}
