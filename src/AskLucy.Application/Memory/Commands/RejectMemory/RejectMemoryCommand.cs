using MediatR;

namespace AskLucy.Application.Memory.Commands.RejectMemory;

/// <summary>contracts/memories-api.md — `POST /api/v1/memories/{id}/actions/reject` (spec.md FR-021, User Story 3 AC3).</summary>
public sealed record RejectMemoryCommand(Guid MemoryId) : IRequest;
