using MediatR;

namespace AskLucy.Application.Memory.Commands.EditMemory;

/// <summary>contracts/memories-api.md — `PUT /api/v1/memories/{id}` (spec.md FR-019, User Story 2 AC2).</summary>
public sealed record EditMemoryCommand(Guid MemoryId, string Content) : IRequest;
