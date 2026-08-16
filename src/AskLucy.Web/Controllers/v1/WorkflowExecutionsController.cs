using AskLucy.Application.Common;
using AskLucy.Application.Workflows;
using AskLucy.Application.Workflows.Commands.ApproveWorkflowNode;
using AskLucy.Application.Workflows.Commands.CancelWorkflowExecution;
using AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;
using AskLucy.Application.Workflows.Commands.RejectWorkflowNode;
using AskLucy.Application.Workflows.Commands.RequestWorkflowNodeChanges;
using AskLucy.Application.Workflows.Commands.ResumeWorkflowExecution;
using AskLucy.Application.Workflows.Commands.RetryWorkflowExecutionNode;
using AskLucy.Application.Workflows.Commands.StartWorkflowExecution;
using AskLucy.Application.Workflows.Queries.GetWorkflowApproval;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecution;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecutionEvents;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecutionNodes;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecutionUsage;
using AskLucy.Application.Workflows.Queries.ListWorkflowExecutions;
using AskLucy.Domain.Workflows;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Workflow execution runtime (contracts/workflows-api.md). Every operation is implicitly scoped to the caller (spec.md FR-059/SC-008).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("workflow-endpoints")]
[Route("api/v1/workflow-executions")]
public sealed class WorkflowExecutionsController(ISender mediator) : ControllerBase
{
    /// <summary>Never finishes synchronously (spec.md FR-047) — the run continues in the background; poll <see cref="Get"/> for progress.</summary>
    [HttpPost]
    public async Task<ActionResult<WorkflowExecutionSummaryDto>> Start([FromBody] StartWorkflowExecutionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new StartWorkflowExecutionCommand(request.WorkflowId, request.WorkflowVersionNumber, request.InputsJson, request.TriggerType),
            cancellationToken);

        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowExecutionDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowExecutionQuery(id), cancellationToken));

    /// <summary>Execution history — cursor-paginated (spec.md User Story 8, contracts/workflows-api.md).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkflowExecutionSummaryDto>>> List(
        [FromQuery] Guid? workflowId, [FromQuery] WorkflowExecutionStatus? status, [FromQuery] WorkflowExecutionTriggerType? triggerType,
        [FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListWorkflowExecutionsQuery(workflowId, status, triggerType, cursor, pageSize), cancellationToken));

    [HttpGet("{id:guid}/nodes")]
    public async Task<ActionResult<IReadOnlyList<WorkflowExecutionNodeDto>>> GetNodes(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowExecutionNodesQuery(id), cancellationToken));

    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<WorkflowExecutionUsageDto>> GetUsage(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowExecutionUsageQuery(id), cancellationToken));

    [HttpGet("{id:guid}/approvals/{approvalId:guid}")]
    public async Task<ActionResult<WorkflowApprovalDto>> GetApproval(Guid id, Guid approvalId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowApprovalQuery(id, approvalId), cancellationToken));

    /// <summary>Resumes the paused execution in the background — never finishes synchronously (spec.md FR-047).</summary>
    [HttpPost("{id:guid}/approvals/{approvalId:guid}/approve")]
    public async Task<ActionResult<WorkflowApprovalDto>> ApproveApproval(Guid id, Guid approvalId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ApproveWorkflowNodeCommand(id, approvalId), cancellationToken));

    [HttpPost("{id:guid}/approvals/{approvalId:guid}/reject")]
    public async Task<ActionResult<WorkflowApprovalDto>> RejectApproval(Guid id, Guid approvalId, [FromBody] RejectWorkflowNodeRequest? request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RejectWorkflowNodeCommand(id, approvalId, request?.Reason), cancellationToken));

    [HttpPost("{id:guid}/approvals/{approvalId:guid}/request-changes")]
    public async Task<ActionResult<WorkflowApprovalDto>> RequestChanges(Guid id, Guid approvalId, [FromBody] RequestWorkflowNodeChangesRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RequestWorkflowNodeChangesCommand(id, approvalId, request.Comments), cancellationToken));

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<WorkflowExecutionEventDto>>> GetEvents(Guid id, [FromQuery] DateTime? since, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowExecutionEventsQuery(id, since), cancellationToken));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new PauseWorkflowExecutionCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Resumes a user-paused execution in the background — never finishes synchronously (spec.md FR-047).</summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResumeWorkflowExecutionCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new CancelWorkflowExecutionCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Manual retry of a `Failed` node (spec.md User Story 7) — resumes the execution in the background, never finishes synchronously.</summary>
    [HttpPost("{id:guid}/nodes/{nodeId:guid}/retry")]
    public async Task<IActionResult> RetryNode(Guid id, Guid nodeId, CancellationToken cancellationToken)
    {
        await mediator.Send(new RetryWorkflowExecutionNodeCommand(id, nodeId), cancellationToken);
        return NoContent();
    }
}
