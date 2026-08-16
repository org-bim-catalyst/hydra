using MediatR;

namespace AskLucy.Application.Workflows.Commands.UpdateWorkflowPolicy;

public sealed record UpdateWorkflowPolicyCommand(Guid Id, string Name, string? Description, string? ConditionsJson, bool IsEnabled) : IRequest<WorkflowPolicyDto>;
