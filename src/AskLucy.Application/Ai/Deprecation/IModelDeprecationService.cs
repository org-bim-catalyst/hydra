using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>specs/069 FR-001..FR-011: deprecates vendor-removed models and moves platform defaults off them.</summary>
public interface IModelDeprecationService
{
    Task<ModelDeprecationResult> DeprecateAsync(
        Guid providerId,
        IReadOnlyList<DeprecationCandidate> candidates,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed record DeprecationCandidate(AIModel Model, ReplacementChoice Choice);

public sealed record ModelDeprecationResult(Guid? BatchId, IReadOnlyList<string> DeprecatedModelKeys, IReadOnlyList<SyncApplyFailureDto> Failed);

public enum ReplacementChoiceKind
{
    Proposed,
    Model,
    None,
}

/// <summary>An administrator's replacement decision for one retired model (specs/069 FR-007).</summary>
public sealed record ReplacementChoice(ReplacementChoiceKind Kind, Guid? ModelId = null)
{
    public static ReplacementChoice Proposed { get; } = new(ReplacementChoiceKind.Proposed);
}
