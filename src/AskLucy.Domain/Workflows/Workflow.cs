using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowType
{
    Manual,
    EventDriven,
    AgentAssisted,

    /// <summary>Reserved for a future release (spec.md FR-065) — never selectable via <see cref="Workflow.SetEventTriggerConfiguration"/> or <see cref="Workflow.Create"/> today.</summary>
    Scheduled,
}

public enum WorkflowStatus
{
    Draft,
    Published,
    Archived,
    Disabled,
    Deprecated,
}

/// <summary>Input spec for one node, resolved by <see cref="Workflow.Publish"/> into a <see cref="WorkflowNode"/> (research.md Decision 19 — the draft canvas JSON is parsed into these before Publish is called; Domain never parses raw JSON itself).</summary>
public sealed record WorkflowNodeSpec(
    string NodeKey,
    WorkflowNodeType NodeType,
    string Name,
    string? Description,
    string InputSchemaJson,
    string OutputSchemaJson,
    string ConfigurationJson,
    string RequiredPermissionsJson,
    int? TimeoutSeconds,
    string? RetryPolicyJson,
    WorkflowNodeApprovalPolicy ApprovalPolicy,
    string? IdempotencyKeyExpression,
    string? CompensatingNodeKey,
    double CanvasX,
    double CanvasY);

/// <summary>Input spec for one connection, resolved by <see cref="Workflow.Publish"/> into a <see cref="WorkflowConnection"/> — <see cref="SourceNodeKey"/>/<see cref="TargetNodeKey"/> reference <see cref="WorkflowNodeSpec.NodeKey"/>, not a database id (research.md Decision 19).</summary>
public sealed record WorkflowConnectionSpec(string SourceNodeKey, string TargetNodeKey, string? BranchLabel, string? TypeContract);

/// <summary>Input spec for one variable, resolved by <see cref="Workflow.Publish"/> into a <see cref="WorkflowVariable"/>.</summary>
public sealed record WorkflowVariableSpec(string Name, WorkflowVariableKind Kind, WorkflowVariableType ValueType, string? DefaultValueJson, bool IsRequired);

/// <summary>
/// The reusable, user-owned orchestration definition (spec.md FR-001-FR-004, data-model.md).
/// Aggregate root for the <c>Workflows</c> bounded context — owns its <see cref="WorkflowVersion"/>
/// history. Publishing materializes <see cref="DraftDefinitionJson"/> into structured, immutable
/// <see cref="WorkflowNode"/>/<see cref="WorkflowConnection"/>/<see cref="WorkflowVariable"/> rows
/// on a new <see cref="WorkflowVersion"/> (research.md Decision 19); executions always reference
/// that snapshot, never this mutable draft.
/// </summary>
public sealed class Workflow : BaseEntity
{
    private readonly List<WorkflowVersion> _versions = [];

    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public WorkflowType WorkflowType { get; private set; }

    public WorkflowStatus Status { get; private set; }

    /// <summary>Set when transitioning to <see cref="WorkflowStatus.Archived"/>; cleared on <see cref="Restore"/> — mirrors <c>Agent.PreArchiveStatus</c> so a workflow archived directly from Draft restores back to Draft rather than incorrectly landing on Published.</summary>
    public WorkflowStatus? PreArchiveStatus { get; private set; }

    /// <summary>The mutable canvas document — nodes, connections, variables, layout (research.md Decision 19). Only this field changes while editing; published versions never read it.</summary>
    public string DraftDefinitionJson { get; private set; } = "{}";

    public int? PublishedVersionNumber { get; private set; }

    /// <summary>Populated only when <see cref="WorkflowType"/> is <see cref="Workflows.WorkflowType.EventDriven"/> (FR-064).</summary>
    public string? EventTriggerConfigurationJson { get; private set; }

    public IReadOnlyCollection<WorkflowVersion> Versions => _versions;

    private Workflow()
    {
        // Required by EF Core materialization.
    }

    public static Workflow Create(string ownerId, string name, string? description, WorkflowType workflowType, string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A workflow must have an owner.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A workflow name is required.");
        }

        if (workflowType == WorkflowType.Scheduled)
        {
            throw new DomainRuleViolationException("Scheduled workflows are not available in this release (FR-065).");
        }

        return new Workflow
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            Description = description,
            WorkflowType = workflowType,
            Status = WorkflowStatus.Draft,
            DraftDefinitionJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Draft-field edit (FR-001/FR-003/FR-009) — never touches published version history.</summary>
    public void UpdateDraft(string name, string? description, string draftDefinitionJson, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A workflow name is required.");
        }

        Name = name.Trim();
        Description = description;
        DraftDefinitionJson = draftDefinitionJson;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-063/FR-064 — configures (or clears, by passing <c>null</c>) the event trigger; only meaningful when <see cref="WorkflowType"/> is <see cref="Workflows.WorkflowType.EventDriven"/>.</summary>
    public void SetEventTriggerConfiguration(string? eventTriggerConfigurationJson, string actor)
    {
        if (eventTriggerConfigurationJson is not null && WorkflowType != WorkflowType.EventDriven)
        {
            throw new DomainRuleViolationException("An event trigger can only be configured on an Event-Driven workflow.");
        }

        EventTriggerConfigurationJson = eventTriggerConfigurationJson;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>
    /// Publishes an immutable snapshot of the current draft (FR-012-FR-016, research.md Decision
    /// 19). The caller (a command handler) has already parsed <see cref="DraftDefinitionJson"/>
    /// and run <c>WorkflowGraphValidator</c> against it — this method assumes the specs are valid
    /// and only enforces the invariants a materialized version itself must hold (unique node keys,
    /// resolvable connection/compensation references).
    /// </summary>
    public WorkflowVersion Publish(
        IReadOnlyList<WorkflowNodeSpec> nodes,
        IReadOnlyList<WorkflowConnectionSpec> connections,
        IReadOnlyList<WorkflowVariableSpec> variables,
        string inputsSchemaJson,
        string outputsSchemaJson,
        string errorPolicyJson,
        string executionPolicyJson,
        string securityPolicyJson,
        string? changeDescription,
        string actor)
    {
        if (nodes.Count == 0)
        {
            throw new DomainRuleViolationException("A workflow must have at least one node before it can be published.");
        }

        if (nodes.Select(n => n.NodeKey).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
        {
            throw new DomainRuleViolationException("Every node in a workflow must have a unique key.");
        }

        var nextVersionNumber = (PublishedVersionNumber ?? 0) + 1;
        var version = WorkflowVersion.Create(Id, nextVersionNumber, inputsSchemaJson, outputsSchemaJson, errorPolicyJson, executionPolicyJson, securityPolicyJson, actor, changeDescription);

        var nodeIdByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var spec in nodes)
        {
            var node = version.AddNode(spec);
            nodeIdByKey[spec.NodeKey] = node.Id;
        }

        foreach (var spec in nodes)
        {
            if (spec.CompensatingNodeKey is null)
            {
                continue;
            }

            if (!nodeIdByKey.TryGetValue(spec.CompensatingNodeKey, out var compensatingNodeId))
            {
                throw new DomainRuleViolationException($"Node '{spec.NodeKey}' declares a compensating node '{spec.CompensatingNodeKey}' that does not exist.");
            }

            version.SetNodeCompensation(nodeIdByKey[spec.NodeKey], compensatingNodeId);
        }

        foreach (var spec in connections)
        {
            if (!nodeIdByKey.TryGetValue(spec.SourceNodeKey, out var sourceId) || !nodeIdByKey.TryGetValue(spec.TargetNodeKey, out var targetId))
            {
                throw new DomainRuleViolationException("A connection references a node that does not exist.");
            }

            version.AddConnection(sourceId, targetId, spec.BranchLabel, spec.TypeContract);
        }

        foreach (var spec in variables)
        {
            version.AddVariable(spec);
        }

        _versions.Add(version);
        PublishedVersionNumber = nextVersionNumber;
        Status = WorkflowStatus.Published;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;

        return version;
    }

    /// <summary>Copies the current draft only, never version/execution history (FR-003), into a brand-new workflow in Draft status.</summary>
    public Workflow Duplicate(string actor)
    {
        var copy = Create(OwnerId, $"{Name} (Copy)", Description, WorkflowType, actor);
        copy.DraftDefinitionJson = DraftDefinitionJson;
        return copy;
    }

    /// <summary>Archiving is allowed from any status (FR-003 doesn't restrict it to Published) — <see cref="PreArchiveStatus"/> records where to return on <see cref="Restore"/>.</summary>
    public void Archive(string actor)
    {
        if (Status == WorkflowStatus.Archived)
        {
            return;
        }

        PreArchiveStatus = Status;
        Status = WorkflowStatus.Archived;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Restore(string actor)
    {
        Status = PreArchiveStatus ?? WorkflowStatus.Draft;
        PreArchiveStatus = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-002; stops event-trigger dispatch (Acceptance Scenario 9.3) without discarding the published definition — reversible via <see cref="Enable"/>.</summary>
    public void Disable(string actor)
    {
        if (Status != WorkflowStatus.Published)
        {
            throw new DomainRuleViolationException("Only a published workflow can be disabled.");
        }

        Status = WorkflowStatus.Disabled;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Enable(string actor)
    {
        if (Status != WorkflowStatus.Disabled)
        {
            throw new DomainRuleViolationException("Only a disabled workflow can be enabled.");
        }

        Status = WorkflowStatus.Published;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-002 — a one-way lifecycle stage: no new manual or event-triggered executions start; already-running executions are unaffected.</summary>
    public void Deprecate(string actor)
    {
        if (Status != WorkflowStatus.Published)
        {
            throw new DomainRuleViolationException("Only a published workflow can be deprecated.");
        }

        Status = WorkflowStatus.Deprecated;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
