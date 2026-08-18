using MediatR;

namespace AskLucy.Application.Memory.Commands.ApproveMemory;

/// <summary>contracts/memories-api.md — `POST /api/v1/memories/{id}/actions/approve` (spec.md FR-021, User Story 3 AC2).</summary>
public sealed record ApproveMemoryCommand(Guid MemoryId) : IRequest;
