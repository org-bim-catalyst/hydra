using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// An immutable, published snapshot of an <see cref="Agent"/>'s configuration (spec.md
/// FR-007-FR-010, data-model.md). Created only via <see cref="Agent.Publish"/> — never
/// constructed directly by Application-layer code. Append-only: no update/delete methods.
/// Executions reference this snapshot, never the mutable <see cref="Agent"/> draft, so a later
/// draft edit can never change what an already-started execution is running.
/// </summary>
public sealed class AgentVersion : BaseEntity
{
    public Guid AgentId { get; private set; }

    public int VersionNumber { get; private set; }

    public AgentInstructions Instructions { get; private set; } = AgentInstructions.Empty;

    /// <summary>
    /// specs/045 research.md D9 — nullable since the conversational runtime. Null means "resolve
    /// at run time from the owning agent's <c>ModelCapability</c>", which is the only honest state
    /// for a platform-provisioned agent: the AI catalog is administrator-configured and may be
    /// empty on a fresh deployment, and pinning whatever was default at provisioning time records
    /// a fact that goes stale the moment the assignment changes. Both are null together or both
    /// non-null; <c>Agent.Publish</c> still requires both for user agents, so nothing about the
    /// existing flow changed.
    /// </summary>
    public Guid? ModelProviderId { get; private set; }

    /// <inheritdoc cref="ModelProviderId"/>
    public Guid? ModelId { get; private set; }

    public AgentExecutionPolicy ExecutionPolicy { get; private set; } = AgentExecutionPolicy.Empty;

    public AgentOutputFormat OutputFormat { get; private set; }

    public string ToolsSnapshotJson { get; private set; } = "[]";

    public string KnowledgeBasesSnapshotJson { get; private set; } = "[]";

    public string? MemoryPolicySnapshotJson { get; private set; }

    public string? ChangeDescription { get; private set; }

    /// <summary>
    /// specs/045 FR-035 — SHA-256 of the <c>SystemAgentDefinition</c> that produced this version.
    /// Null for user-published versions. Provisioning compares it against the newest version's
    /// hash and publishes only on a difference, which is what makes restarts idempotent and an
    /// upgrade a single new version rather than one per boot.
    /// </summary>
    public string? DefinitionHash { get; private set; }

    private AgentVersion()
    {
        // Required by EF Core materialization.
    }

    internal static AgentVersion Create(
        Guid agentId,
        int versionNumber,
        AgentInstructions instructions,
        Guid? modelProviderId,
        Guid? modelId,
        AgentExecutionPolicy executionPolicy,
        AgentOutputFormat outputFormat,
        string toolsSnapshotJson,
        string knowledgeBasesSnapshotJson,
        string? memoryPolicySnapshotJson,
        string? changeDescription,
        string actor,
        string? definitionHash = null) => new()
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            VersionNumber = versionNumber,
            Instructions = instructions,
            ModelProviderId = modelProviderId,
            ModelId = modelId,
            ExecutionPolicy = executionPolicy,
            OutputFormat = outputFormat,
            ToolsSnapshotJson = toolsSnapshotJson,
            KnowledgeBasesSnapshotJson = knowledgeBasesSnapshotJson,
            MemoryPolicySnapshotJson = memoryPolicySnapshotJson,
            ChangeDescription = changeDescription,
            DefinitionHash = definitionHash,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
