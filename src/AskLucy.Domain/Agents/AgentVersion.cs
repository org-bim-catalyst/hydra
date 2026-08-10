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

    public Guid ModelProviderId { get; private set; }

    public Guid ModelId { get; private set; }

    public AgentExecutionPolicy ExecutionPolicy { get; private set; } = AgentExecutionPolicy.Empty;

    public AgentOutputFormat OutputFormat { get; private set; }

    public string ToolsSnapshotJson { get; private set; } = "[]";

    public string KnowledgeBasesSnapshotJson { get; private set; } = "[]";

    public string? MemoryPolicySnapshotJson { get; private set; }

    public string? ChangeDescription { get; private set; }

    private AgentVersion()
    {
        // Required by EF Core materialization.
    }

    internal static AgentVersion Create(
        Guid agentId,
        int versionNumber,
        AgentInstructions instructions,
        Guid modelProviderId,
        Guid modelId,
        AgentExecutionPolicy executionPolicy,
        AgentOutputFormat outputFormat,
        string toolsSnapshotJson,
        string knowledgeBasesSnapshotJson,
        string? memoryPolicySnapshotJson,
        string? changeDescription,
        string actor) => new()
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
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
