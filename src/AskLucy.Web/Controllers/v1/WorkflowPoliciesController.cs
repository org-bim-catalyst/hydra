using AskLucy.Application.Workflows;
using AskLucy.Application.Workflows.Commands.CreateWorkflowPolicy;
using AskLucy.Application.Workflows.Commands.DeleteWorkflowPolicy;
using AskLucy.Application.Workflows.Commands.SetWorkflowUserExecutionLimit;
using AskLucy.Application.Workflows.Commands.UpdateWorkflowPolicy;
using AskLucy.Application.Workflows.Queries.ListWorkflowPolicies;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Administrator-managed auto-approval policy CRUD for the workflow engine's platform-mandatory approval baseline (spec.md "Approval Policies") — Administrator/Super User only.</summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/workflow-policies")]
public sealed class WorkflowPoliciesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowPolicyDto>>> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListWorkflowPoliciesQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<WorkflowPolicyDto>> Create([FromBody] CreateWorkflowPolicyRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new CreateWorkflowPolicyCommand(request.Name, request.Description, request.WorkflowNodeType, request.UnderlyingToolName, request.ConditionsJson),
            cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowPolicyDto>> Update(Guid id, [FromBody] UpdateWorkflowPolicyRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdateWorkflowPolicyCommand(id, request.Name, request.Description, request.ConditionsJson, request.IsEnabled), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteWorkflowPolicyCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>FR-069/FR-070 — per-user override of the concurrent-execution cap.</summary>
    [HttpPut("user-limits/{userId}")]
    public async Task<ActionResult<WorkflowUserExecutionLimitDto>> SetUserExecutionLimit(
        string userId, [FromBody] SetWorkflowUserExecutionLimitRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetWorkflowUserExecutionLimitCommand(userId, request.MaxConcurrentExecutions), cancellationToken));
}
