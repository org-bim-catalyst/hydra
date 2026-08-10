using MediatR;

namespace AskLucy.Application.Agents.Commands.ResumeAgentExecution;

/// <summary>Resumes a user-paused execution (spec.md User Story 4) — re-enqueues the runner. For a `WaitingForApproval` execution, use the approval endpoints instead (an approval decision is what resumes those).</summary>
public sealed record ResumeAgentExecutionCommand(Guid ExecutionId) : IRequest;
