using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.CreateAgentPolicy;
using AskLucy.Application.Agents.Commands.DeleteAgentPolicy;
using AskLucy.Application.Agents.Commands.SetAgentUserExecutionLimit;
using AskLucy.Application.Agents.Commands.UpdateAgentPolicy;
using AskLucy.Application.Agents.Queries.ListAgentPolicies;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Administrator-managed auto-approval policy CRUD (spec.md FR-025/FR-026, research.md Decision 1) — permission-gated (specs/055-role-management research.md Decision 5).</summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/agent-policies")]
public sealed class AgentPoliciesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("admin.agent-policies.view")]
    public async Task<ActionResult<IReadOnlyList<AgentPolicyDto>>> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListAgentPoliciesQuery(), cancellationToken));

    [HttpPost]
    [RequirePermission("admin.agent-policies.manage")]
    public async Task<ActionResult<AgentPolicyDto>> Create([FromBody] CreateAgentPolicyRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CreateAgentPolicyCommand(request.Name, request.Description, request.ToolName, request.ConditionsJson), cancellationToken));

    [HttpPut("{id:guid}")]
    [RequirePermission("admin.agent-policies.manage")]
    public async Task<ActionResult<AgentPolicyDto>> Update(Guid id, [FromBody] UpdateAgentPolicyRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdateAgentPolicyCommand(id, request.Name, request.Description, request.ConditionsJson, request.IsEnabled), cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequirePermission("admin.agent-policies.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteAgentPolicyCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPut("user-limits/{userId}")]
    [RequirePermission("admin.agent-policies.manage")]
    public async Task<ActionResult<AgentUserExecutionLimitDto>> SetUserExecutionLimit(
        string userId, [FromBody] SetAgentUserExecutionLimitRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetAgentUserExecutionLimitCommand(userId, request.MaxConcurrentExecutions), cancellationToken));
}
