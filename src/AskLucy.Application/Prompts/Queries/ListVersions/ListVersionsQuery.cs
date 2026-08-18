using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListVersions;

/// <summary>Every version of a prompt, newest first (spec.md FR-032).</summary>
public sealed record ListVersionsQuery(Guid PromptId) : IRequest<IReadOnlyList<PromptVersionSummaryDto>>;
