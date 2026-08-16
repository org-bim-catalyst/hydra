using MediatR;

namespace AskLucy.Application.Workflows.Commands.ResumeWorkflowExecution;

/// <summary>Resumes a user-paused execution (spec.md User Story 6) — never used for a <c>WaitingForApproval</c> execution, which resumes via its approval decision instead.</summary>
public sealed record ResumeWorkflowExecutionCommand(Guid ExecutionId) : IRequest;
