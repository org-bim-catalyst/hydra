namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 FR-012/FR-014: counts, finds and rewrites the end-user items that name a model.
/// Lives in Persistence because the rewrite spans five aggregates and the workflow draft JSON.
/// </summary>
public interface IModelReferenceStore
{
    Task<ModelImpactCounts> CountAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default);

    /// <summary>Owners with at least one live item naming any of the models, in stable order.</summary>
    Task<IReadOnlyList<string>> ListOwnerIdsAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites every live item these owners hold that still names one of <paramref name="replacements"/>,
    /// and records one <c>ItemSwitch</c> per rewrite. Owners with nothing to rewrite are omitted.
    /// </summary>
    Task<IReadOnlyList<OwnerSwitchResult>> SwitchAsync(
        Guid batchId,
        IReadOnlyDictionary<Guid, RetiredModelTarget> replacements,
        IReadOnlyList<string> ownerIds,
        string actor,
        DateTime atUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ModelImpactCounts(int Conversations, int Agents, int Prompts, int WorkflowSteps, int DistinctUsers)
{
    public static ModelImpactCounts Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Where one retired model's items go: its replacement's model, provider and key, and its deprecation record.</summary>
public sealed record RetiredModelTarget(Guid DeprecationId, Guid ModelId, Guid ProviderId, string ModelKey, string DisplayName);

public sealed record OwnerSwitchResult(string OwnerUserId, IReadOnlyList<OwnerSwitchItem> Items);

public sealed record OwnerSwitchItem(
    Guid PreviousModelId,
    string PreviousModelName,
    Guid NewModelId,
    string NewModelName,
    string ItemLabel,
    ModelImpactKind Kind);

public enum ModelImpactKind
{
    Conversation,
    Agent,
    Prompt,
    WorkflowStep,
    UserDefault,
}
