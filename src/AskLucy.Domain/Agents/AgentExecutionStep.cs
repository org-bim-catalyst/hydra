using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentExecutionStepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Cancelled,
    WaitingForApproval,
}

public enum AgentExecutionStepType
{
    ToolCall,
    ModelReasoning,
    Validation,
}

/// <summary>One step within an execution's plan (spec.md FR-013/FR-014, data-model.md) — child of <see cref="AgentExecution"/>, reachable only via its <c>Steps</c> navigation.</summary>
public sealed class AgentExecutionStep : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public int StepIndex { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public AgentExecutionStepType StepType { get; private set; }

    public AgentExecutionStepStatus Status { get; private set; } = AgentExecutionStepStatus.Pending;

    public Guid? DependsOnStepId { get; private set; }

    public string? InputJson { get; private set; }

    public string? OutputJson { get; private set; }

    public string? ToolName { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public Guid? ErrorId { get; private set; }

    private AgentExecutionStep()
    {
        // Required by EF Core materialization.
    }

    internal static AgentExecutionStep Create(
        Guid agentExecutionId, int stepIndex, string description, AgentExecutionStepType stepType,
        Guid? dependsOnStepId, string? toolName, string? inputJson) => new()
        {
            Id = Guid.CreateVersion7(),
            AgentExecutionId = agentExecutionId,
            StepIndex = stepIndex,
            Description = description,
            StepType = stepType,
            Status = AgentExecutionStepStatus.Pending,
            DependsOnStepId = dependsOnStepId,
            ToolName = toolName,
            InputJson = inputJson,
            CreatedAtUtc = DateTime.UtcNow,
        };

    public void Start()
    {
        Status = AgentExecutionStepStatus.Running;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void Complete(string? outputJson)
    {
        Status = AgentExecutionStepStatus.Completed;
        OutputJson = outputJson;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(Guid errorId)
    {
        Status = AgentExecutionStepStatus.Failed;
        ErrorId = errorId;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Used both for FR-019 conditional skips and for research.md Decision 12 (a mutating-permission tool step is skipped, never executed, during a test execution).</summary>
    public void Skip(string reason)
    {
        Status = AgentExecutionStepStatus.Skipped;
        OutputJson = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = AgentExecutionStepStatus.Cancelled;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void WaitForApproval()
    {
        Status = AgentExecutionStepStatus.WaitingForApproval;
    }
}
