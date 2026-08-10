using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

public enum PromptRatingValue
{
    Good,
    NeedsImprovement,
    Failed,
}

/// <summary>A manual evaluation of a <see cref="PromptExecution"/>'s result (spec.md FR-044).</summary>
public sealed class PromptRating : BaseEntity
{
    public Guid PromptExecutionId { get; private set; }

    public PromptRatingValue RatingValue { get; private set; }

    public string RatedByActor { get; private set; } = string.Empty;

    private PromptRating()
    {
        // Required by EF Core materialization.
    }

    public static PromptRating Create(Guid promptExecutionId, PromptRatingValue ratingValue, string actor)
    {
        return new PromptRating
        {
            Id = Guid.CreateVersion7(),
            PromptExecutionId = promptExecutionId,
            RatingValue = ratingValue,
            RatedByActor = actor,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Update(PromptRatingValue ratingValue, string actor)
    {
        RatingValue = ratingValue;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
