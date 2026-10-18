using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>specs/069 data-model: one confirmed admin action (a sync confirmation or a replacement change).</summary>
public sealed class ModelDeprecationBatch : BaseEntity
{
    public Guid ProviderId { get; private set; }

    public ModelDeprecationKind Kind { get; private set; }

    public string ConfirmedByUserId { get; private set; } = string.Empty;

    public DateTime ConfirmedAtUtc { get; private set; }

    public UserImpactStatus UserImpactStatus { get; private set; } = UserImpactStatus.Pending;

    public DateTime? UserImpactCompletedAtUtc { get; private set; }

    public int NotifiedUserCount { get; private set; }

    public Guid? ReplacementChangeOfDeprecationId { get; private set; }

    /// <summary>Kind <c>ReplacementChange</c> only: the replacement that was in use before the change.</summary>
    public Guid? PreviousReplacementModelId { get; private set; }

    private readonly List<ModelDeprecation> _deprecations = [];

    public IReadOnlyCollection<ModelDeprecation> Deprecations => _deprecations;

    private ModelDeprecationBatch()
    {
    }

    public static ModelDeprecationBatch Deprecate(Guid providerId, string confirmedByUserId, DateTime confirmedAtUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ProviderId = providerId,
            Kind = ModelDeprecationKind.Deprecation,
            ConfirmedByUserId = confirmedByUserId,
            ConfirmedAtUtc = confirmedAtUtc,
            CreatedAtUtc = confirmedAtUtc,
            CreatedBy = confirmedByUserId,
        };

    public static ModelDeprecationBatch ReplacementChange(
        Guid providerId, Guid deprecationId, Guid previousReplacementModelId, string confirmedByUserId, DateTime confirmedAtUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ProviderId = providerId,
            Kind = ModelDeprecationKind.ReplacementChange,
            ConfirmedByUserId = confirmedByUserId,
            ConfirmedAtUtc = confirmedAtUtc,
            ReplacementChangeOfDeprecationId = deprecationId,
            PreviousReplacementModelId = previousReplacementModelId,
            CreatedAtUtc = confirmedAtUtc,
            CreatedBy = confirmedByUserId,
        };

    public void Add(ModelDeprecation deprecation)
    {
        ArgumentNullException.ThrowIfNull(deprecation);
        if (Kind != ModelDeprecationKind.Deprecation)
        {
            throw new DomainRuleViolationException("Only a deprecation batch can hold deprecations.");
        }

        _deprecations.Add(deprecation);
    }

    public void MarkUserImpactCompleted(int notifiedUserCount, DateTime completedAtUtc)
    {
        if (UserImpactStatus != UserImpactStatus.Pending)
        {
            throw new DomainRuleViolationException("User impact has already completed.");
        }

        UserImpactStatus = UserImpactStatus.Completed;
        UserImpactCompletedAtUtc = completedAtUtc;
        NotifiedUserCount = notifiedUserCount;
    }
}
