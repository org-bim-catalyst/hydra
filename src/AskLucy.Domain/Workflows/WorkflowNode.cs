using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>FR-018 — the full node type catalog. <see cref="Delay"/> is an architectural placeholder for future scheduling (spec.md).</summary>
public enum WorkflowNodeType
{
    Start,
    End,
    AiPrompt,
    AiAgent,
    RagSearch,
    MemorySearch,
    DocumentProcessing,
    FileOperation,
    McpTool,
    NativeTool,
    Transform,
    Condition,
    Parallel,
    Merge,
    HumanApproval,
    Validation,
    Delay,
}

/// <summary>FR-035 — a node's own approval-policy declaration; can only make approval *more* strict than the platform-mandatory baseline (FR-036), never less (research.md Decision 5).</summary>
public enum WorkflowNodeApprovalPolicy
{
    AlwaysRequire,
    NeverRequire,
    AboveRiskLevel,
    ForThisNodeType,
}

/// <summary>Retry configuration (FR-040); a null field falls back to the system-wide default at execution time.</summary>
public sealed record WorkflowRetryPolicy(
    int? MaxAttempts,
    int? InitialDelaySeconds,
    int? MaxDelaySeconds,
    string? BackoffStrategy,
    string? RetryableErrorTypesJson,
    string? NonRetryableErrorTypesJson)
{
    public static readonly WorkflowRetryPolicy Empty = new(null, null, null, null, null, null);
}

/// <summary>
/// A single step within a <see cref="WorkflowVersion"/>'s graph (FR-017/FR-018, data-model.md).
/// Immutable once created — belongs to exactly one <see cref="WorkflowVersion"/>, never a mutable
/// draft (research.md Decision 19).
/// </summary>
public sealed class WorkflowNode : BaseEntity
{
    public Guid WorkflowVersionId { get; private set; }

    /// <summary>Stable identifier from the draft canvas — what <c>{{steps.node_key.field}}</c> references (FR-025); unique within a version.</summary>
    public string NodeKey { get; private set; } = string.Empty;

    public WorkflowNodeType NodeType { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string InputSchemaJson { get; private set; } = "{}";

    public string OutputSchemaJson { get; private set; } = "{}";

    public string ConfigurationJson { get; private set; } = "{}";

    public string RequiredPermissionsJson { get; private set; } = "[]";

    /// <summary>Null falls back to <c>WorkflowRuntimeOptions.DefaultNodeTimeoutSeconds</c> (FR-041).</summary>
    public int? TimeoutSeconds { get; private set; }

    public string? RetryPolicyJson { get; private set; }

    public WorkflowNodeApprovalPolicy ApprovalPolicy { get; private set; }

    /// <summary>FR-043, research.md Decision 13 — evaluated by the expression engine before a mutating retry.</summary>
    public string? IdempotencyKeyExpression { get; private set; }

    /// <summary>FR-042, research.md Decision 14 — another node in the same version to execute under the <c>Compensate</c> failure strategy; validated at publish time to not be self-referential.</summary>
    public Guid? CompensatingNodeId { get; private set; }

    /// <summary>Designer layout only, no execution semantics.</summary>
    public double CanvasX { get; private set; }

    public double CanvasY { get; private set; }

    private WorkflowNode()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowNode Create(
        Guid workflowVersionId, string nodeKey, WorkflowNodeType nodeType, string name, string? description,
        string inputSchemaJson, string outputSchemaJson, string configurationJson, string requiredPermissionsJson,
        int? timeoutSeconds, string? retryPolicyJson, WorkflowNodeApprovalPolicy approvalPolicy,
        string? idempotencyKeyExpression, double canvasX, double canvasY)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            throw new DomainRuleViolationException("A node key is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A node name is required.");
        }

        return new WorkflowNode
        {
            Id = Guid.CreateVersion7(),
            WorkflowVersionId = workflowVersionId,
            NodeKey = nodeKey,
            NodeType = nodeType,
            Name = name,
            Description = description,
            InputSchemaJson = inputSchemaJson,
            OutputSchemaJson = outputSchemaJson,
            ConfigurationJson = configurationJson,
            RequiredPermissionsJson = requiredPermissionsJson,
            TimeoutSeconds = timeoutSeconds,
            RetryPolicyJson = retryPolicyJson,
            ApprovalPolicy = approvalPolicy,
            IdempotencyKeyExpression = idempotencyKeyExpression,
            CanvasX = canvasX,
            CanvasY = canvasY,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    internal void SetCompensatingNode(Guid compensatingNodeId) => CompensatingNodeId = compensatingNodeId;
}
