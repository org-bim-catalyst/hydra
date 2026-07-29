using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A source reference associated with an assistant <see cref="Message"/> (FR-017). Child of
/// <see cref="Message"/>'s aggregate, not independently reachable (constitution &#167;5) —
/// created only via <see cref="Message.AddCitation"/>.
/// </summary>
public sealed class Citation : BaseEntity
{
    public Guid MessageId { get; private set; }

    public string SourceLabel { get; private set; } = string.Empty;

    public string? SourceReference { get; private set; }

    private Citation()
    {
        // Required by EF Core materialization.
    }

    internal static Citation Create(Guid messageId, string sourceLabel, string? sourceReference, string actor)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            throw new DomainRuleViolationException("A citation source label is required.");
        }

        return new Citation
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            SourceLabel = sourceLabel,
            SourceReference = sourceReference,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
