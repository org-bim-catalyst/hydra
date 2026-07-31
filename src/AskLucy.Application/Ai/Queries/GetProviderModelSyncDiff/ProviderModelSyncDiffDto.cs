using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;

/// <summary>One catalog model the vendor no longer lists — enough for the UI to display and for apply (specs/008-ai-model-catalog-management ApplyProviderModelSyncCommand) to target by <see cref="Id"/>.</summary>
public sealed record RemovedModelDto(Guid Id, string ModelKey, string DisplayName);

/// <summary>
/// contracts/admin-ai-models.md — the read-only result of a sync check (FR-005/006), and
/// also the shape the client echoes back to `.../sync/apply` (no server-side ephemeral
/// cache — same pattern as spec 005's model-comparison "continue" endpoint).
/// </summary>
public sealed record ProviderModelSyncDiffDto(IReadOnlyList<ProviderModelInfo> Added, IReadOnlyList<RemovedModelDto> RemovedFromVendor);
