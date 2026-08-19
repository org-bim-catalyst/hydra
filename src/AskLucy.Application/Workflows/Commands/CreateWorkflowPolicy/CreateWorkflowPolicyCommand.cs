using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.CreateWorkflowPolicy;

/// <summary>Administrator-managed auto-approval rule for the platform's mandatory approval baseline (spec.md "Approval Policies") — Administrator/Super User only, enforced by the controller's <c>AdministratorOrSuperUser</c> authorization policy.</summary>
public sealed record CreateWorkflowPolicyCommand(string Name, string? Description, WorkflowNodeType? WorkflowNodeType, string? UnderlyingToolName, string? ConditionsJson) : IRequest<WorkflowPolicyDto>;
