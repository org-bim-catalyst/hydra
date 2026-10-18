using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Queries.GetModelDeprecations;

/// <summary>specs/069 contracts §3: counts only — never user identities or item contents.</summary>
public sealed record ModelDeprecationSummaryDto(
    Guid Id,
    Guid ModelId,
    string ModelKey,
    string DisplayName,
    Guid ProviderId,
    string ProviderName,
    Guid? ReplacementModelId,
    string? ReplacementDisplayName,
    ReplacementValidationStatus ValidationStatus,
    int ReassignedCount,
    int OpenFlagCount,
    UserImpactStatus UserImpactStatus,
    int NotifiedUserCount,
    DateTime ConfirmedAtUtc,
    string ConfirmedByUserId);

public sealed record ModelDeprecationDetailDto(
    Guid Id,
    Guid BatchId,
    Guid ModelId,
    string ModelKey,
    string DisplayName,
    Guid ProviderId,
    string ProviderName,
    DeprecationReason Reason,
    DateTime ConfirmedAtUtc,
    string ConfirmedByUserId,
    Guid? ReplacementModelId,
    string? ReplacementDisplayName,
    ReplacementSource ReplacementSource,
    ReplacementValidationStatus ValidationStatus,
    string? ValidatedByUserId,
    DateTime? ValidatedAtUtc,
    UserImpactStatus UserImpactStatus,
    int NotifiedUserCount,
    IReadOnlyList<PlatformDefaultChangeDto> PlatformDefaults);

public sealed record PlatformDefaultChangeDto(
    DefaultTargetKind TargetKind,
    AiCapability? Capability,
    DefaultChangeOutcome Outcome,
    Guid? NewModelId,
    string? NewModelDisplayName,
    DefaultFlagReason? FlagReason,
    Guid? ServingProviderId,
    Guid? ServingModelId,
    bool IsOpenFlag,
    string? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    Guid? ResolutionModelId);

public sealed record ModelDeprecationPageDto(IReadOnlyList<ModelDeprecationSummaryDto> Items, Guid? NextCursor);
