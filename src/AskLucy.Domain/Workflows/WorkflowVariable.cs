using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowVariableKind
{
    WorkflowVariable,
    NodeOutputReference,
    UserInput,
    EnvironmentConfiguration,
    SystemContext,
}

/// <summary>FR-026 — the supported variable types.</summary>
// CA1720: not renamed — this enum represents a workflow variable's value-type kind, and "String"
// is the correct, idiomatic name for that kind; it is also persisted by member name (EF-mapped
// column/JSON), so renaming the member would be a breaking change independent of the type name.
#pragma warning disable CA1720
public enum WorkflowVariableType
{
    String,
    Number,
    Boolean,
    Date,
    Json,
    Text,
    File,
    Document,
    Collection,
}
#pragma warning restore CA1720

/// <summary>A typed value scoped to a <see cref="WorkflowVersion"/> (FR-026, data-model.md). Immutable once created.</summary>
public sealed class WorkflowVariable : BaseEntity
{
    public Guid WorkflowVersionId { get; private set; }

    /// <summary>Unique within a version.</summary>
    public string Name { get; private set; } = string.Empty;

    public WorkflowVariableKind Kind { get; private set; }

    public WorkflowVariableType ValueType { get; private set; }

    public string? DefaultValueJson { get; private set; }

    /// <summary>Only meaningful for <see cref="WorkflowVariableKind.UserInput"/> — enforced when starting an execution.</summary>
    public bool IsRequired { get; private set; }

    private WorkflowVariable()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowVariable Create(Guid workflowVersionId, string name, WorkflowVariableKind kind, WorkflowVariableType valueType, string? defaultValueJson, bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A variable name is required.");
        }

        return new WorkflowVariable
        {
            Id = Guid.CreateVersion7(),
            WorkflowVersionId = workflowVersionId,
            Name = name,
            Kind = kind,
            ValueType = valueType,
            DefaultValueJson = defaultValueJson,
            IsRequired = isRequired,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }
}
