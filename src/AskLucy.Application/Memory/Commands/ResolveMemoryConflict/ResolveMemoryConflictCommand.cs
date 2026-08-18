using MediatR;

namespace AskLucy.Application.Memory.Commands.ResolveMemoryConflict;

/// <summary>contracts/memories-api.md's three resolution outcomes (User Story 6 AC3, clarified 2026-08-09).</summary>
public enum MemoryConflictResolution
{
    KeepExisting,
    KeepNew,
    KeepBoth,
}

/// <summary>contracts/memories-api.md — `POST /api/v1/memories/{id}/actions/resolve-conflict` (spec.md FR-016, User Story 6 AC2). <paramref name="MemoryId"/> is either side of the open conflict — <see cref="AskLucy.Domain.Memory.MemoryConflict.ExistingMemoryId"/> or <see cref="AskLucy.Domain.Memory.MemoryConflict.NewMemoryId"/>.</summary>
public sealed record ResolveMemoryConflictCommand(Guid MemoryId, MemoryConflictResolution Resolution) : IRequest;
