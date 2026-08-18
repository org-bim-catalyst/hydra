using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentExecutionEventType
{
    ExecutionStarted,
    PlanCreated,
    StepStarted,
    StepCompleted,
    StepFailed,
    ToolCallStarted,
    ToolCallCompleted,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalRejected,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    UsageUpdated,
}

/// <summary>
/// Append-only, safe-metadata-only event (spec.md FR-034/FR-035, data-model.md) — the persisted
/// backing store the real-time hub (contracts/agent-execution-events.md) replays/pushes from.
/// Never carries chain-of-thought or raw prompt/tool content.
/// </summary>
public sealed class AgentExecutionEvent : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public Guid AgentVersionId { get; private set; }

    public Guid? StepId { get; private set; }

    public AgentExecutionEventType EventType { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public string? SafeMetadataJson { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private AgentExecutionEvent()
    {
        // Required by EF Core materialization.
    }

    internal static AgentExecutionEvent Create(
        Guid agentExecutionId, Guid agentVersionId, Guid? stepId, AgentExecutionEventType eventType,
        string status, string? safeMetadataJson) => new()
        {
            Id = Guid.CreateVersion7(),
            AgentExecutionId = agentExecutionId,
            AgentVersionId = agentVersionId,
            StepId = stepId,
            EventType = eventType,
            Status = status,
            SafeMetadataJson = safeMetadataJson,
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };
}
