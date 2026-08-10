using MediatR;

namespace AskLucy.Application.Prompts.Queries.CompareVersions;

/// <summary>A field-by-field diff between two versions of the same prompt (spec.md FR-032, User Story 3 AC2).</summary>
public sealed record CompareVersionsQuery(Guid PromptId, int FromVersionNumber, int ToVersionNumber) : IRequest<PromptVersionComparisonDto>;
