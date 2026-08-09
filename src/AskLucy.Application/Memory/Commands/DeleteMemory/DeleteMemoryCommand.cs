using MediatR;

namespace AskLucy.Application.Memory.Commands.DeleteMemory;

/// <summary>contracts/memories-api.md — `DELETE /api/v1/memories/{id}` (spec.md FR-020, User Story 2 AC3).</summary>
public sealed record DeleteMemoryCommand(Guid MemoryId) : IRequest;
