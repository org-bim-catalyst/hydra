using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListPrompts;

/// <summary>
/// Cursor-based search/filter over the caller's own prompts (spec.md FR-050–FR-053,
/// contracts/prompts-api.md). Started as User Story 1's "basic list" (view + free-text query
/// only); this is the same query/handler/endpoint extended with category/tag/folder/status
/// filters for User Story 4 (T090), not a second list endpoint.
/// </summary>
public sealed record ListPromptsQuery(
    PromptListView View,
    string? Query,
    Guid? CategoryId,
    string? Tag,
    Guid? FolderId,
    PromptStatus? Status,
    string? Cursor,
    int PageSize) : IRequest<PagedResult<PromptListItemDto>>;
