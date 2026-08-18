using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetExecution;

/// <summary>Full detail view of one execution, including its rating if any (spec.md FR-042, FR-044).</summary>
public sealed record GetExecutionQuery(Guid ExecutionId) : IRequest<PromptExecutionDetailDto>;
