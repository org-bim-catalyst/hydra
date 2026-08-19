using AskLucy.Application.Common;
using AskLucy.Application.Workflows;
using AskLucy.Application.Workflows.Commands.ArchiveWorkflow;
using AskLucy.Application.Workflows.Commands.CreateWorkflow;
using AskLucy.Application.Workflows.Commands.DeleteWorkflow;
using AskLucy.Application.Workflows.Commands.DeprecateWorkflow;
using AskLucy.Application.Workflows.Commands.DisableWorkflow;
using AskLucy.Application.Workflows.Commands.DuplicateWorkflow;
using AskLucy.Application.Workflows.Commands.EnableWorkflow;
using AskLucy.Application.Workflows.Commands.PublishWorkflowVersion;
using AskLucy.Application.Workflows.Commands.RestoreWorkflow;
using AskLucy.Application.Workflows.Commands.UpdateWorkflow;
using AskLucy.Application.Workflows.Commands.ValidateWorkflow;
using AskLucy.Application.Workflows.Queries.GetWorkflow;
using AskLucy.Application.Workflows.Queries.GetWorkflowStatistics;
using AskLucy.Application.Workflows.Queries.GetWorkflowVersion;
using AskLucy.Application.Workflows.Queries.ListWorkflows;
using AskLucy.Application.Workflows.Queries.ListWorkflowVersions;
using AskLucy.Domain.Workflows;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Workflow definition/lifecycle/versioning (contracts/workflows-api.md). Every operation is implicitly scoped to the caller (spec.md FR-059).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("workflow-endpoints")]
[Route("api/v1/workflows")]
public sealed class WorkflowsController(ISender mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkflowDetailDto>> Create([FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreateWorkflowCommand(request.Name, request.Description, request.WorkflowType, request.EventTriggerConfigurationJson), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowQuery(id), cancellationToken));

    /// <summary>Workflow Monitoring dashboard aggregate, scoped to the caller's own executions (spec.md User Story 8).</summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<WorkflowStatisticsDto>> GetStatistics(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowStatisticsQuery(), cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkflowListItemDto>>> List(
        [FromQuery] WorkflowStatus? status = null,
        [FromQuery] WorkflowType? workflowType = null,
        [FromQuery] string? search = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListWorkflowsQuery(status, workflowType, search, cursor, pageSize), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowDetailDto>> Update(Guid id, [FromBody] UpdateWorkflowRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdateWorkflowCommand(id, request.Name, request.Description, request.DraftDefinitionJson, request.EventTriggerConfigurationJson), cancellationToken));

    [HttpPost("{id:guid}/actions/validate")]
    public async Task<ActionResult<IReadOnlyList<WorkflowValidationIssueDto>>> Validate(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ValidateWorkflowCommand(id), cancellationToken));

    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<WorkflowVersionDto>> Publish(Guid id, [FromBody] PublishWorkflowVersionRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PublishWorkflowVersionCommand(id, request.ChangeDescription), cancellationToken));

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    public async Task<ActionResult<WorkflowVersionDto>> GetVersion(Guid id, int versionNumber, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetWorkflowVersionQuery(id, versionNumber), cancellationToken));

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<WorkflowVersionDto>>> ListVersions(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListWorkflowVersionsQuery(id), cancellationToken));

    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<WorkflowDetailDto>> Duplicate(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DuplicateWorkflowCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<ActionResult<WorkflowDetailDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ArchiveWorkflowCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/restore")]
    public async Task<ActionResult<WorkflowDetailDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreWorkflowCommand(id), cancellationToken));

    /// <summary>FR-002 — stops event-trigger dispatch (Acceptance Scenario 9.3); manual starts remain allowed.</summary>
    [HttpPost("{id:guid}/actions/disable")]
    public async Task<ActionResult<WorkflowDetailDto>> Disable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DisableWorkflowCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/enable")]
    public async Task<ActionResult<WorkflowDetailDto>> Enable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new EnableWorkflowCommand(id), cancellationToken));

    /// <summary>FR-002 — a one-way lifecycle stage; no new manual or event-triggered executions start afterward.</summary>
    [HttpPost("{id:guid}/actions/deprecate")]
    public async Task<ActionResult<WorkflowDetailDto>> Deprecate(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DeprecateWorkflowCommand(id), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteWorkflowCommand(id), cancellationToken);
        return NoContent();
    }
}
