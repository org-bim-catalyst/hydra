using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>specs/069 data-model: one end-user item moved off a retired model.</summary>
public sealed class ItemSwitch : BaseEntity
{
    public Guid BatchId { get; private set; }

    public Guid DeprecationId { get; private set; }

    public string OwnerUserId { get; private set; } = string.Empty;

    public SwitchedItemKind ItemKind { get; private set; }

    public Guid ItemId { get; private set; }

    public string? NodeId { get; private set; }

    public string ItemLabel { get; private set; } = string.Empty;

    public Guid PreviousModelId { get; private set; }

    public Guid NewModelId { get; private set; }

    private ItemSwitch()
    {
    }

    public static ItemSwitch Create(
        Guid batchId,
        Guid deprecationId,
        string ownerUserId,
        SwitchedItemKind itemKind,
        Guid itemId,
        string? nodeId,
        string itemLabel,
        Guid previousModelId,
        Guid newModelId,
        DateTime atUtc)
    {
        if (previousModelId == newModelId)
        {
            throw new DomainRuleViolationException("An item cannot be switched to the model it already uses.");
        }

        return new ItemSwitch
        {
            Id = Guid.CreateVersion7(),
            BatchId = batchId,
            DeprecationId = deprecationId,
            OwnerUserId = ownerUserId,
            ItemKind = itemKind,
            ItemId = itemId,
            NodeId = nodeId,
            ItemLabel = itemLabel.Length <= 200 ? itemLabel : itemLabel[..200],
            PreviousModelId = previousModelId,
            NewModelId = newModelId,
            CreatedAtUtc = atUtc,
            CreatedBy = "system",
        };
    }
}
