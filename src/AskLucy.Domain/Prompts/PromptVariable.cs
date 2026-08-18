using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

public enum PromptVariableType
{
    String,
    Number,
    Boolean,
    Date,
    Json,
    Text,
    File,
    Conversation,
    KnowledgeBase,
}

/// <summary>Input shape for defining a variable at prompt-create/edit time (spec.md FR-010-FR-012) — not itself persisted, mapped into a <see cref="PromptVariable"/> row per <see cref="PromptVersion"/>.</summary>
public sealed record PromptVariableDefinition(
    string Name,
    string? Description,
    PromptVariableType VariableType,
    bool IsRequired,
    string? DefaultValue,
    string? ExampleValue,
    string? ValidationRulesJson,
    int OrderIndex);

/// <summary>
/// A named placeholder definition scoped to one <see cref="PromptVersion"/> (spec.md
/// FR-010-FR-014, data-model.md). Immutable once created — a content edit creates a new
/// <see cref="PromptVersion"/> with its own fresh set of variable rows, never mutates a prior
/// version's variables.
/// </summary>
public sealed class PromptVariable : BaseEntity
{
    public Guid PromptVersionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public PromptVariableType VariableType { get; private set; }

    public bool IsRequired { get; private set; }

    public string? DefaultValue { get; private set; }

    public string? ExampleValue { get; private set; }

    public string? ValidationRulesJson { get; private set; }

    public int OrderIndex { get; private set; }

    private PromptVariable()
    {
        // Required by EF Core materialization.
    }

    internal static PromptVariable Create(Guid promptVersionId, PromptVariableDefinition definition, string actor)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new DomainRuleViolationException("A variable name is required.");
        }

        return new PromptVariable
        {
            Id = Guid.CreateVersion7(),
            PromptVersionId = promptVersionId,
            Name = definition.Name.Trim(),
            Description = definition.Description,
            VariableType = definition.VariableType,
            IsRequired = definition.IsRequired,
            DefaultValue = definition.DefaultValue,
            ExampleValue = definition.ExampleValue,
            ValidationRulesJson = definition.ValidationRulesJson,
            OrderIndex = definition.OrderIndex,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
