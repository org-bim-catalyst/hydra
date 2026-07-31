namespace AskLucy.Application.Ai.Commands.ApplyProviderModelSync;

/// <summary>One selected row that could not be applied because it was stale by the time this call ran (specs/009-selective-model-sync-review FR-007a/FR-007b).</summary>
public sealed record SyncApplyFailureDto(string ModelKey, string DisplayName, string Reason);

/// <summary>
/// contracts/selective-sync-apply.md — the result of a best-effort apply. A row appears in
/// exactly one of <see cref="AppliedModelKeys"/> or <see cref="Failed"/>, never both.
/// </summary>
public sealed record ApplyProviderModelSyncResultDto(IReadOnlyList<string> AppliedModelKeys, IReadOnlyList<SyncApplyFailureDto> Failed);
