using MediatR;

namespace AskLucy.Application.Memory.Commands.ClearAllMemories;

/// <summary>contracts/memory-privacy-api.md — `POST /api/v1/memories/actions/clear-all` (spec.md FR-023, User Story 4 AC2, SC-003). Irreversible — requires explicit confirmation, not a bare delete.</summary>
public sealed record ClearAllMemoriesCommand(bool Confirm) : IRequest;
