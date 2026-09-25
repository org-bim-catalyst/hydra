using AskLucy.Domain.Common;

namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// One recorded failure inside an incident. Append-only: after insert, only retention (delete)
/// and account erasure (anonymise) write to it.
/// </summary>
public sealed class OperationalFailureOccurrence : BaseEntity
{
    public Guid IncidentId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public OperationalFailureSeverity Severity { get; private set; }

    public OperationalFailureKind Kind { get; private set; }

    public OperationalFailureEngine Engine { get; private set; }

    public string Operation { get; private set; } = string.Empty;

    public string? ProviderName { get; private set; }

    public string? Model { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public bool IsFailover { get; private set; }

    public string? UserId { get; private set; }

    public Guid? ChatId { get; private set; }

    public Guid? MessageId { get; private set; }

    public Guid? WorkflowId { get; private set; }

    public Guid? WorkflowExecutionId { get; private set; }

    public Guid? WorkflowExecutionNodeId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? KnowledgeBaseId { get; private set; }

    public Guid? AgentId { get; private set; }

    public Guid? AgentExecutionId { get; private set; }

    public Guid? McpServerId { get; private set; }

    public string? JobId { get; private set; }

    public string? SourceIp { get; private set; }

    public bool IsUserErased { get; private set; }

    private OperationalFailureOccurrence()
    {
        // Required by EF Core materialization.
    }

    public static OperationalFailureOccurrence Create(
        Guid incidentId,
        DateTime occurredAtUtc,
        OperationalFailureSeverity severity,
        OperationalFailureKind kind,
        OperationalFailureEngine engine,
        string operation,
        string reason,
        string correlationId,
        OperationalFailureReferences references,
        string? providerName = null,
        string? model = null,
        bool isFailover = false,
        string? sourceIp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(references);

        return new OperationalFailureOccurrence
        {
            Id = Guid.CreateVersion7(),
            IncidentId = incidentId,
            OccurredAtUtc = occurredAtUtc,
            Severity = severity,
            Kind = kind,
            Engine = engine,
            Operation = operation,
            Reason = reason,
            CorrelationId = correlationId,
            ProviderName = providerName,
            Model = model,
            IsFailover = isFailover,
            SourceIp = sourceIp,
            UserId = references.UserId,
            ChatId = references.ChatId,
            MessageId = references.MessageId,
            WorkflowId = references.WorkflowId,
            WorkflowExecutionId = references.WorkflowExecutionId,
            WorkflowExecutionNodeId = references.WorkflowExecutionNodeId,
            DocumentId = references.DocumentId,
            KnowledgeBaseId = references.KnowledgeBaseId,
            AgentId = references.AgentId,
            AgentExecutionId = references.AgentExecutionId,
            McpServerId = references.McpServerId,
            JobId = references.JobId,
        };
    }
}
