using MediatR;

namespace AskLucy.Application.Prompts.Queries.CompareExecutions;

/// <summary>Full detail DTOs for the requested execution ids, side by side (spec.md FR-045, SC-009).</summary>
public sealed record CompareExecutionsQuery(IReadOnlyList<Guid> ExecutionIds) : IRequest<IReadOnlyList<PromptExecutionDetailDto>>;
