using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Queries.ListMemories;

/// <summary>contracts/memories-api.md — the Memory Center list (spec.md FR-017, FR-018, User Story 2 AC1).</summary>
public sealed record MemoryListItemDto(
    Guid Id, string Category, string Content, string State, bool IsSensitive, Guid? ProjectId, string? ProjectName,
    string SourceType, Guid? SourceConversationId, decimal Importance, decimal Confidence,
    DateTime LastReinforcedAtUtc, DateTime CreatedAtUtc);

public sealed record MemoryListResult(IReadOnlyList<MemoryListItemDto> Results, string? NextCursor, int TotalCount);

/// <summary>
/// <paramref name="ProjectId"/>/<paramref name="GeneralOnly"/> mirror <see cref="AskLucy.Application.Abstractions.IMemoryRepository.SearchAsync"/>'s
/// three-way project scoping (contracts/memories-api.md's <c>projectId=</c> query parameter:
/// omitted = every scope, <c>general</c> = <paramref name="GeneralOnly"/> true, a real id =
/// <paramref name="ProjectId"/> set) — the controller is responsible for parsing the literal
/// <c>"general"</c> string into <paramref name="GeneralOnly"/> before constructing this query.
/// </summary>
public sealed record ListMemoriesQuery(
    MemoryCategory? Category, MemoryLifecycleState? State, Guid? ProjectId, bool GeneralOnly,
    string? Query, string? Cursor, int PageSize = 50) : IRequest<MemoryListResult>;
