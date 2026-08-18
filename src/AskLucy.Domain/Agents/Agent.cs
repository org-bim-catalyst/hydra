using System.Text.Json;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentType
{
    Conversational,
    Research,
    Document,
    Knowledge,
    Task,
}

public enum AgentStatus
{
    Draft,
    Published,
    Archived,
}

public enum AgentOutputFormat
{
    PlainText,
    Markdown,
    Json,
    StructuredOutput,
    Files,
}

/// <summary>Instruction categories an agent's system prompt is composed from (spec.md FR-004).</summary>
public sealed record AgentInstructions(
    string? SystemInstructions,
    string? Objectives,
    string? Constraints,
    string? BehavioralRules,
    string? OutputRequirements,
    string? ToolUsageRules,
    string? SafetyRules)
{
    public static readonly AgentInstructions Empty = new(null, null, null, null, null, null, null);
}

/// <summary>Execution limits (spec.md FR-040); a null field falls back to the system-wide default (<c>AgentRuntimeOptions</c>) at execution time.</summary>
public sealed record AgentExecutionPolicy(
    int? MaxSteps,
    int? MaxExecutionDurationSeconds,
    int? MaxTokens,
    decimal? MaxCost,
    int? MaxToolCalls,
    int? MaxRetries)
{
    public static readonly AgentExecutionPolicy Empty = new(null, null, null, null, null, null);
}

/// <summary>
/// The reusable, user-owned agent definition (spec.md FR-001-FR-006, data-model.md). Aggregate
/// root for the <c>Agents</c> bounded context — owns its <see cref="AgentVersion"/> history and
/// draft <see cref="AgentTool"/>/<see cref="AgentKnowledgeBase"/>/<see cref="AgentMemoryPolicy"/>
/// configuration. Publishing snapshots the current draft into an immutable <see cref="AgentVersion"/>
/// (FR-007-FR-010); executions always reference that snapshot, never this mutable draft.
/// </summary>
public sealed class Agent : BaseEntity
{
    private readonly List<AgentTool> _tools = [];
    private readonly List<AgentKnowledgeBase> _knowledgeBases = [];
    private readonly List<AgentVersion> _versions = [];

    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public AgentType AgentType { get; private set; }

    public AgentStatus Status { get; private set; }

    /// <summary>Set when transitioning to <see cref="AgentStatus.Archived"/>; cleared on <see cref="Restore"/> — lets an agent archived directly from <see cref="AgentStatus.Draft"/> restore back to Draft rather than incorrectly landing on Published.</summary>
    public AgentStatus? PreArchiveStatus { get; private set; }

    public AgentInstructions Instructions { get; private set; } = AgentInstructions.Empty;

    public Guid? ModelProviderId { get; private set; }

    public Guid? ModelId { get; private set; }

    public AgentOutputFormat OutputFormat { get; private set; } = AgentOutputFormat.PlainText;

    public AgentExecutionPolicy ExecutionPolicy { get; private set; } = AgentExecutionPolicy.Empty;

    public int? PublishedVersionNumber { get; private set; }

    public AgentMemoryPolicy? MemoryPolicy { get; private set; }

    public IReadOnlyCollection<AgentTool> Tools => _tools;

    public IReadOnlyCollection<AgentKnowledgeBase> KnowledgeBases => _knowledgeBases;

    public IReadOnlyCollection<AgentVersion> Versions => _versions;

    private Agent()
    {
        // Required by EF Core materialization.
    }

    public static Agent Create(
        string ownerId,
        string name,
        string? description,
        AgentType agentType,
        AgentInstructions instructions,
        Guid? modelProviderId,
        Guid? modelId,
        AgentOutputFormat outputFormat,
        AgentExecutionPolicy executionPolicy,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("An agent must have an owner.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("An agent name is required.");
        }

        return new Agent
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            Description = description,
            AgentType = agentType,
            Status = AgentStatus.Draft,
            Instructions = instructions,
            ModelProviderId = modelProviderId,
            ModelId = modelId,
            OutputFormat = outputFormat,
            ExecutionPolicy = executionPolicy,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Draft-field edit (spec.md FR-001/FR-003) — never touches published version history.</summary>
    public void UpdateDraft(
        string name,
        string? description,
        AgentType agentType,
        AgentInstructions instructions,
        Guid? modelProviderId,
        Guid? modelId,
        AgentOutputFormat outputFormat,
        AgentExecutionPolicy executionPolicy,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("An agent name is required.");
        }

        Name = name.Trim();
        Description = description;
        AgentType = agentType;
        Instructions = instructions;
        ModelProviderId = modelProviderId;
        ModelId = modelId;
        OutputFormat = outputFormat;
        ExecutionPolicy = executionPolicy;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public AgentTool AddTool(string toolName, string? configurationJson, string actor)
    {
        var tool = AgentTool.Create(Id, toolName, configurationJson, actor);
        _tools.Add(tool);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
        return tool;
    }

    public void ClearTools(string actor)
    {
        _tools.Clear();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public AgentKnowledgeBase AddKnowledgeBase(Guid knowledgeBaseId, string actor)
    {
        var link = AgentKnowledgeBase.Create(Id, knowledgeBaseId, actor);
        _knowledgeBases.Add(link);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
        return link;
    }

    public void ClearKnowledgeBases(string actor)
    {
        _knowledgeBases.Clear();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetMemoryPolicy(bool allowRead, bool allowWriteProposals, string? preApprovedCategoriesJson, string actor)
    {
        MemoryPolicy = AgentMemoryPolicy.Create(Id, allowRead, allowWriteProposals, preApprovedCategoriesJson, actor);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Publishes an immutable snapshot of the current draft (spec.md FR-007-FR-010).</summary>
    public AgentVersion Publish(string? changeDescription, string actor)
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Instructions.SystemInstructions))
        {
            throw new DomainRuleViolationException("An agent must have a name and system instructions before it can be published.");
        }

        if (ModelProviderId is null || ModelId is null)
        {
            throw new DomainRuleViolationException("An agent must have a model selected before it can be published.");
        }

        var nextVersionNumber = (PublishedVersionNumber ?? 0) + 1;
        var toolsSnapshotJson = JsonSerializer.Serialize(_tools.Select(t => new { t.ToolName, t.ConfigurationJson }));
        var knowledgeBasesSnapshotJson = JsonSerializer.Serialize(_knowledgeBases.Select(k => k.KnowledgeBaseId));
        var memoryPolicySnapshotJson = MemoryPolicy is null
            ? null
            : JsonSerializer.Serialize(new { MemoryPolicy.AllowRead, MemoryPolicy.AllowWriteProposals, MemoryPolicy.PreApprovedCategoriesJson });

        var version = AgentVersion.Create(
            Id, nextVersionNumber, Instructions, ModelProviderId.Value, ModelId.Value, ExecutionPolicy,
            OutputFormat, toolsSnapshotJson, knowledgeBasesSnapshotJson, memoryPolicySnapshotJson, changeDescription, actor);

        _versions.Add(version);
        PublishedVersionNumber = nextVersionNumber;
        Status = AgentStatus.Published;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;

        return version;
    }

    /// <summary>Copies the current draft only, never version/execution history (spec.md User Story 6, Acceptance Scenario 4), into a brand-new agent in Draft status.</summary>
    public Agent Duplicate(string actor)
    {
        var copy = Create(
            OwnerId, $"{Name} (Copy)", Description, AgentType, Instructions, ModelProviderId, ModelId,
            OutputFormat, ExecutionPolicy, actor);

        foreach (var tool in _tools)
        {
            copy.AddTool(tool.ToolName, tool.ConfigurationJson, actor);
        }

        foreach (var knowledgeBase in _knowledgeBases)
        {
            copy.AddKnowledgeBase(knowledgeBase.KnowledgeBaseId, actor);
        }

        if (MemoryPolicy is not null)
        {
            copy.SetMemoryPolicy(MemoryPolicy.AllowRead, MemoryPolicy.AllowWriteProposals, MemoryPolicy.PreApprovedCategoriesJson, actor);
        }

        return copy;
    }

    /// <summary>Archiving is allowed from any status (spec.md FR-003 doesn't restrict it to Published) — <see cref="PreArchiveStatus"/> records where to return on <see cref="Restore"/>.</summary>
    public void Archive(string actor)
    {
        if (Status == AgentStatus.Archived)
        {
            return;
        }

        PreArchiveStatus = Status;
        Status = AgentStatus.Archived;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Restore(string actor)
    {
        Status = PreArchiveStatus ?? AgentStatus.Draft;
        PreArchiveStatus = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
