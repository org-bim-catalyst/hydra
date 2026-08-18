using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListExecutions;

/// <summary>Cursor-paginated execution history for one prompt, newest first (spec.md FR-042).</summary>
public sealed record ListExecutionsQuery(Guid PromptId, string? Cursor, int PageSize) : IRequest<PagedResult<PromptExecutionSummaryDto>>;
