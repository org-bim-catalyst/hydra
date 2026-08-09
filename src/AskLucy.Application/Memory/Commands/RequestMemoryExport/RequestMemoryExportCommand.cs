using MediatR;

namespace AskLucy.Application.Memory.Commands.RequestMemoryExport;

/// <summary>contracts/memory-privacy-api.md — `POST /api/v1/memories/actions/export` (spec.md FR-024, User Story 4 AC3).</summary>
public sealed record RequestMemoryExportCommand : IRequest<Guid>;
