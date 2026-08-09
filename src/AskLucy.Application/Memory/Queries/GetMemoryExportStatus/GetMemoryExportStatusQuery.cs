using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryExportStatus;

/// <summary>contracts/memory-privacy-api.md — `GET /api/v1/memories/exports/{exportJobId}`. <see cref="StoredFileName"/> is the <c>IFileStorage</c> handle the controller signs into a download URL; only ever present once <see cref="Status"/> is <c>Ready</c> (never exposed as a raw path itself).</summary>
public sealed record MemoryExportStatusDto(string Status, string? StoredFileName);

public sealed record GetMemoryExportStatusQuery(Guid ExportJobId) : IRequest<MemoryExportStatusDto>;
