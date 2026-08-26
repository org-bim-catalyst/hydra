using AskLucy.Application.Ai.Commands.SendChatMessage;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution data-model.md — the full result of one
/// <see cref="IBoundaryResolutionService.ResolveAsync"/> call. Field names deliberately match
/// <c>LocationResolutionOutcome</c>'s shape (same discriminated-outcome idiom, constitution §VIII
/// — never throws into the calling chat turn).
/// </summary>
public enum BoundaryResolutionOutcomeType
{
    Confirmed,
    NoCandidates,
    Unavailable,
}

/// <summary>
/// <see cref="ConfirmedBoundary"/> is non-null for <see cref="BoundaryResolutionOutcomeType.Confirmed"/>
/// AND <see cref="BoundaryResolutionOutcomeType.NoCandidates"/> (the latter carries a
/// Low-confidence, manual-fallback approximation — User Story 1 acceptance scenario 3 requires an
/// actual rendered area, not just an apologetic sentence with nothing shown). It is null only for
/// <see cref="BoundaryResolutionOutcomeType.Unavailable"/> — FR-012 forbids returning a default
/// result when the data source itself couldn't be reached.
/// </summary>
public sealed record BoundaryResolutionOutcome(
    BoundaryResolutionOutcomeType Type,
    ConfirmedSiteBoundaryData? ConfirmedBoundary,
    string? ConfirmationText);
