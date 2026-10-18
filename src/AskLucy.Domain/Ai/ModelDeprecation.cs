using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>specs/069 FR-002: the deprecation record for one retired model.</summary>
public sealed class ModelDeprecation : BaseEntity
{
    public Guid BatchId { get; private set; }

    public Guid ModelId { get; private set; }

    public DeprecationReason Reason { get; private set; }

    public Guid? ReplacementModelId { get; private set; }

    public ReplacementSource ReplacementSource { get; private set; }

    public ReplacementValidationStatus ValidationStatus { get; private set; }

    public string? ValidatedByUserId { get; private set; }

    public DateTime? ValidatedAtUtc { get; private set; }

    private readonly List<PlatformDefaultChange> _defaultChanges = [];

    public IReadOnlyCollection<PlatformDefaultChange> DefaultChanges => _defaultChanges;

    private ModelDeprecation()
    {
    }

    public static ModelDeprecation Create(
        Guid batchId,
        Guid modelId,
        Guid? replacementModelId,
        ReplacementSource source,
        string actor,
        DateTime createdAtUtc)
    {
        if (replacementModelId == modelId)
        {
            throw new DomainRuleViolationException("A model cannot replace itself.");
        }

        var hasReplacement = replacementModelId is not null;
        return new ModelDeprecation
        {
            Id = Guid.CreateVersion7(),
            BatchId = batchId,
            ModelId = modelId,
            Reason = DeprecationReason.RemovedFromVendor,
            ReplacementModelId = replacementModelId,
            ReplacementSource = hasReplacement ? source : ReplacementSource.None,
            ValidationStatus = hasReplacement ? ReplacementValidationStatus.AwaitingValidation : ReplacementValidationStatus.NotApplicable,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = actor,
        };
    }

    public void AddDefaultChange(PlatformDefaultChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _defaultChanges.Add(change);
    }

    public void Accept(string userId, DateTime atUtc)
    {
        RequireStatus(ReplacementValidationStatus.AwaitingValidation);
        ValidationStatus = ReplacementValidationStatus.Accepted;
        ValidatedByUserId = userId;
        ValidatedAtUtc = atUtc;
    }

    public void ChangeReplacement(Guid newReplacementModelId, string userId, DateTime atUtc)
    {
        RequireStatus(ReplacementValidationStatus.AwaitingValidation);
        if (newReplacementModelId == ModelId)
        {
            throw new DomainRuleViolationException("A model cannot replace itself.");
        }

        ReplacementModelId = newReplacementModelId;
        ReplacementSource = ReplacementSource.AdminChosen;
        ValidationStatus = ReplacementValidationStatus.Changed;
        ValidatedByUserId = userId;
        ValidatedAtUtc = atUtc;
    }

    private void RequireStatus(ReplacementValidationStatus expected)
    {
        if (ValidationStatus != expected)
        {
            throw new DomainRuleViolationException($"The replacement is {ValidationStatus}, not {expected}.");
        }
    }
}
