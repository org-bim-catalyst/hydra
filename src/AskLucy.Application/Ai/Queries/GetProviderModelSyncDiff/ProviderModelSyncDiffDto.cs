using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;

/// <summary>One catalog model the vendor no longer lists — enough for the UI to display and for apply (specs/008-ai-model-catalog-management ApplyProviderModelSyncCommand) to target by <see cref="Id"/>.</summary>
/// <param name="Impact">specs/069 FR-003: counts and the proposed replacement, shown before confirming.</param>
/// <param name="Replacement">specs/069 FR-007: the administrator's choice, echoed back on apply. Null means "proposed".</param>
public sealed record RemovedModelDto(
    Guid Id,
    string ModelKey,
    string DisplayName,
    ModelImpactDto? Impact = null,
    ReplacementRequestDto? Replacement = null);

/// <summary>specs/069 FR-003: counts only, never user identities or item contents.</summary>
public sealed record ModelImpactDto(
    int Conversations,
    int Agents,
    int Prompts,
    int WorkflowSteps,
    int DistinctUsers,
    IReadOnlyList<PlatformDefaultRefDto> PlatformDefaults,
    ReplacementCandidateDto? ProposedReplacement,
    IReadOnlyList<ReplacementCandidateDto> EligibleReplacements);

public sealed record PlatformDefaultRefDto(string TargetKind, string? Capability);

/// <param name="MissingCapabilities">Capabilities the retired model has that this candidate lacks.</param>
public sealed record ReplacementCandidateDto(Guid Id, string DisplayName, IReadOnlyList<string> MissingCapabilities);

/// <summary>specs/069 FR-007: <c>Proposed</c> | <c>Model</c> (with an id) | <c>None</c>.</summary>
public sealed record ReplacementRequestDto(string Kind, Guid? ModelId = null);

/// <summary>
/// contracts/admin-ai-models.md — the read-only result of a sync check (FR-005/006), and
/// also the shape the client echoes back to `.../sync/apply` (no server-side ephemeral
/// cache — same pattern as spec 005's model-comparison "continue" endpoint).
/// </summary>
public sealed record ProviderModelSyncDiffDto(IReadOnlyList<ProviderModelInfo> Added, IReadOnlyList<RemovedModelDto> RemovedFromVendor);
