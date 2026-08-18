using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryReferences;

/// <summary>contracts/memories-api.md — "why does Lucy know this" trace (spec.md FR-014, User Story 1).</summary>
public sealed record MemoryReferenceDto(Guid MemoryId, string Content, decimal RelevanceScore);

public sealed record GetMemoryReferencesQuery(Guid ChatId, Guid MessageId) : IRequest<IReadOnlyList<MemoryReferenceDto>>;
