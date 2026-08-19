using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>Budget policy (FR-055); a null field falls back to the system-wide default (<c>WorkflowRuntimeOptions</c>) at execution time — mirrors <c>AgentExecutionPolicy</c>.</summary>
public sealed record WorkflowExecutionPolicy(
    int? MaxNodeCount,
    int? MaxExecutionDurationSeconds,
    int? MaxTokens,
    decimal? MaxCost,
    int? MaxToolCalls,
    int? MaxParallelNodes,
    int? MaxLoopIterations)
{
    public static readonly WorkflowExecutionPolicy Empty = new(null, null, null, null, null, null, null);
}

/// <summary>Workflow-level failure strategy (FR-039) — applied once a failed node's own <see cref="WorkflowRetryPolicy"/> is exhausted. <see cref="Strategy"/> is one of <c>Stop</c>/<c>Continue</c>/<c>Retry</c>/<c>Fallback</c>/<c>Compensate</c>; an unset or unrecognized value behaves as <c>Stop</c> (the safest default — an execution never silently keeps going by accident).</summary>
public sealed record WorkflowErrorPolicy(string? Strategy)
{
    public static readonly WorkflowErrorPolicy Empty = new((string?)null);

    public string EffectiveStrategy => Strategy ?? "Stop";
}

/// <summary>
/// An immutable, published snapshot of a <see cref="Workflow"/>'s graph (FR-012-FR-016,
/// data-model.md). Created only via <see cref="Workflow.Publish"/> — never constructed directly by
/// Application-layer code. Append-only: no update/delete methods. Executions reference this
/// snapshot, never the mutable <see cref="Workflow.DraftDefinitionJson"/>, so a later draft edit
/// can never change what an already-started execution is running.
/// </summary>
public sealed class WorkflowVersion : BaseEntity
{
    private readonly List<WorkflowNode> _nodes = [];
    private readonly List<WorkflowConnection> _connections = [];
    private readonly List<WorkflowVariable> _variables = [];

    public Guid WorkflowId { get; private set; }

    public int VersionNumber { get; private set; }

    public string InputsSchemaJson { get; private set; } = "{}";

    public string OutputsSchemaJson { get; private set; } = "{}";

    public string ErrorPolicyJson { get; private set; } = "{}";

    public string ExecutionPolicyJson { get; private set; } = "{}";

    public string SecurityPolicyJson { get; private set; } = "{}";

    public string PublishedBy { get; private set; } = string.Empty;

    public string? ChangeDescription { get; private set; }

    public IReadOnlyCollection<WorkflowNode> Nodes => _nodes;

    public IReadOnlyCollection<WorkflowConnection> Connections => _connections;

    public IReadOnlyCollection<WorkflowVariable> Variables => _variables;

    private WorkflowVersion()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowVersion Create(
        Guid workflowId, int versionNumber, string inputsSchemaJson, string outputsSchemaJson,
        string errorPolicyJson, string executionPolicyJson, string securityPolicyJson, string publishedBy, string? changeDescription) => new()
        {
            Id = Guid.CreateVersion7(),
            WorkflowId = workflowId,
            VersionNumber = versionNumber,
            InputsSchemaJson = inputsSchemaJson,
            OutputsSchemaJson = outputsSchemaJson,
            ErrorPolicyJson = errorPolicyJson,
            ExecutionPolicyJson = executionPolicyJson,
            SecurityPolicyJson = securityPolicyJson,
            PublishedBy = publishedBy,
            ChangeDescription = changeDescription,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = publishedBy,
        };

    internal WorkflowNode AddNode(WorkflowNodeSpec spec)
    {
        var node = WorkflowNode.Create(
            Id, spec.NodeKey, spec.NodeType, spec.Name, spec.Description, spec.InputSchemaJson, spec.OutputSchemaJson,
            spec.ConfigurationJson, spec.RequiredPermissionsJson, spec.TimeoutSeconds, spec.RetryPolicyJson,
            spec.ApprovalPolicy, spec.IdempotencyKeyExpression, spec.CanvasX, spec.CanvasY);
        _nodes.Add(node);
        return node;
    }

    internal void SetNodeCompensation(Guid nodeId, Guid compensatingNodeId)
    {
        if (nodeId == compensatingNodeId)
        {
            throw new DomainRuleViolationException("A node cannot compensate itself.");
        }

        var node = _nodes.First(n => n.Id == nodeId);
        node.SetCompensatingNode(compensatingNodeId);
    }

    internal WorkflowConnection AddConnection(Guid sourceNodeId, Guid targetNodeId, string? branchLabel, string? typeContract)
    {
        var connection = WorkflowConnection.Create(Id, sourceNodeId, targetNodeId, branchLabel, typeContract);
        _connections.Add(connection);
        return connection;
    }

    internal WorkflowVariable AddVariable(WorkflowVariableSpec spec)
    {
        var variable = WorkflowVariable.Create(Id, spec.Name, spec.Kind, spec.ValueType, spec.DefaultValueJson, spec.IsRequired);
        _variables.Add(variable);
        return variable;
    }
}
