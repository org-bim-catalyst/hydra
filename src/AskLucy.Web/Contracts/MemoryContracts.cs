using AskLucy.Application.Memory.Commands.ResolveMemoryConflict;
using AskLucy.Domain.Memory;

namespace AskLucy.Web.Contracts;

/// <summary>contracts/memories-api.md — `PUT /api/v1/memories/{id}`.</summary>
public sealed record EditMemoryRequest(string Content);

/// <summary>contracts/memories-api.md — `POST /api/v1/memories/{id}/actions/resolve-conflict` (spec.md FR-016, User Story 6).</summary>
public sealed record ResolveConflictRequest(MemoryConflictResolution Resolution);

/// <summary>contracts/memory-privacy-api.md — one entry in `PUT /api/v1/memories/preferences`'s `categories` array; a partial update (both fields optional).</summary>
public sealed record MemoryCategoryPreferenceUpdateRequest(MemoryCategory Category, MemoryApprovalMode? ApprovalMode, bool? IsEnabled);

/// <summary>contracts/memory-privacy-api.md — `PUT /api/v1/memories/preferences`.</summary>
public sealed record UpdateMemoryPreferencesRequest(bool? MemoryEnabled, IReadOnlyList<MemoryCategoryPreferenceUpdateRequest>? Categories);

/// <summary>contracts/memory-privacy-api.md — `POST /api/v1/memories/actions/export`'s `202 Accepted` body.</summary>
public sealed record MemoryExportJobResponse(Guid ExportJobId);

/// <summary>contracts/memory-privacy-api.md — `GET /api/v1/memories/exports/{exportJobId}`.</summary>
public sealed record MemoryExportStatusResponse(string Status, string? DownloadUrl);
