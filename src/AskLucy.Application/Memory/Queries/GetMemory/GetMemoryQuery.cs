using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemory;

public sealed record MemoryVersionDto(string PreviousContent, string ChangeReason, DateTime ChangedAtUtc, string ChangedByActor);

/// <summary>contracts/memories-api.md's <c>openConflict</c> — the fields needed to render the asynchronous confirmation prompt (FR-016, User Story 6 AC3).</summary>
public sealed record OpenConflictDto(Guid Id, string ConflictType, Guid ExistingMemoryId, Guid? NewMemoryId, DateTime DetectedAtUtc);

public sealed record MemoryDetailDto(
    Guid Id, string Category, string Content, string State, bool IsSensitive, Guid? ProjectId,
    decimal Importance, decimal Confidence, IReadOnlyList<MemoryVersionDto> History, OpenConflictDto? OpenConflict);

public sealed record GetMemoryQuery(Guid MemoryId) : IRequest<MemoryDetailDto>;
