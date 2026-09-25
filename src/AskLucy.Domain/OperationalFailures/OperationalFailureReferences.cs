namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// Soft references from an occurrence to the items involved (FR-012: references only, never
/// content). None is a foreign key, so deleting the item never touches the trail.
/// </summary>
public sealed record OperationalFailureReferences
{
    public static OperationalFailureReferences None { get; } = new();

    public string? UserId { get; init; }

    public Guid? ChatId { get; init; }

    public Guid? MessageId { get; init; }

    public Guid? WorkflowId { get; init; }

    public Guid? WorkflowExecutionId { get; init; }

    public Guid? WorkflowExecutionNodeId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? KnowledgeBaseId { get; init; }

    public Guid? AgentId { get; init; }

    public Guid? AgentExecutionId { get; init; }

    public Guid? McpServerId { get; init; }

    public string? JobId { get; init; }
}
