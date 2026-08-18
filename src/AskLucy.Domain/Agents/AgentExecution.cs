using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentExecutionStatus
{
    Queued,
    Running,
    Paused,
    WaitingForApproval,
    Completed,
    Failed,
    Cancelled,
}

public enum AgentConversationIntegrationMode
{
    Standalone,
    NewConversation,
    ExistingConversation,
}

/// <summary>
/// One run of a specific <see cref="AgentVersion"/> (spec.md FR-009/FR-011, data-model.md).
/// Aggregate root for the execution bounded context — owns its <see cref="AgentExecutionStep"/>/
/// <see cref="AgentExecutionEvent"/>/<see cref="AgentApproval"/>/<see cref="AgentExecutionError"/>
/// history plus <see cref="Usage"/>/<see cref="Cost"/>. Never hard-deleted (FR-050 audit trail);
/// step/event/error rows are append-only once written even though <see cref="Status"/> itself
/// mutates through the execution's lifecycle.
/// </summary>
public sealed class AgentExecution : BaseEntity
{
    private readonly List<AgentExecutionStep> _steps = [];
    private readonly List<AgentExecutionEvent> _events = [];
    private readonly List<AgentApproval> _approvals = [];
    private readonly List<AgentExecutionError> _errors = [];

    public Guid AgentId { get; private set; }

    public Guid AgentVersionId { get; private set; }

    public string RunByUserId { get; private set; } = string.Empty;

    public string Objective { get; private set; } = string.Empty;

    public AgentExecutionStatus Status { get; private set; }

    public bool IsTestExecution { get; private set; }

    public AgentConversationIntegrationMode ConversationIntegrationMode { get; private set; }

    public Guid? UserChatId { get; private set; }

    public string? PlanJson { get; private set; }

    public string? FinalOutputJson { get; private set; }

    public string? FinalOutputText { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? TerminationReason { get; private set; }

    public AgentExecutionUsage? Usage { get; private set; }

    public AgentExecutionCost? Cost { get; private set; }

    public IReadOnlyCollection<AgentExecutionStep> Steps => _steps;

    public IReadOnlyCollection<AgentExecutionEvent> Events => _events;

    public IReadOnlyCollection<AgentApproval> Approvals => _approvals;

    public IReadOnlyCollection<AgentExecutionError> Errors => _errors;

    private AgentExecution()
    {
        // Required by EF Core materialization.
    }

    public static AgentExecution Create(
        Guid agentId,
        Guid agentVersionId,
        string runByUserId,
        string objective,
        bool isTestExecution,
        AgentConversationIntegrationMode conversationIntegrationMode,
        Guid? userChatId,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            throw new DomainRuleViolationException("An execution objective is required.");
        }

        if (conversationIntegrationMode == AgentConversationIntegrationMode.ExistingConversation && userChatId is null)
        {
            throw new DomainRuleViolationException("An existing conversation id is required when the conversation integration mode is ExistingConversation.");
        }

        return new AgentExecution
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            AgentVersionId = agentVersionId,
            RunByUserId = runByUserId,
            Objective = objective,
            Status = AgentExecutionStatus.Queued,
            IsTestExecution = isTestExecution,
            ConversationIntegrationMode = conversationIntegrationMode,
            UserChatId = userChatId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Set once a <c>NewConversation</c>-mode execution's conversation has actually been created (spec.md FR-051/FR-052).</summary>
    public void SetUserChatId(Guid userChatId)
    {
        UserChatId = userChatId;
    }

    public void Start()
    {
        Status = AgentExecutionStatus.Running;
        StartedAtUtc ??= DateTime.UtcNow;
    }

    public void SetPlan(string planJson)
    {
        PlanJson = planJson;
    }

    public AgentExecutionStep AddStep(int stepIndex, string description, AgentExecutionStepType stepType, Guid? dependsOnStepId, string? toolName, string? inputJson)
    {
        var step = AgentExecutionStep.Create(Id, stepIndex, description, stepType, dependsOnStepId, toolName, inputJson);
        _steps.Add(step);
        return step;
    }

    public AgentExecutionEvent RecordEvent(AgentExecutionEventType eventType, Guid agentVersionId, Guid? stepId, string status, string? safeMetadataJson)
    {
        var evt = AgentExecutionEvent.Create(Id, agentVersionId, stepId, eventType, status, safeMetadataJson);
        _events.Add(evt);
        return evt;
    }

    public AgentApproval RequestApproval(Guid? agentToolCallId, string intendedActionDescription, string intendedParametersJson)
    {
        var approval = AgentApproval.CreatePending(Id, agentToolCallId, intendedActionDescription, intendedParametersJson);
        _approvals.Add(approval);
        Status = AgentExecutionStatus.WaitingForApproval;
        return approval;
    }

    public AgentExecutionError RecordError(AgentExecutionErrorCategory category, string message, Guid? stepId, int retryCount)
    {
        var error = AgentExecutionError.Create(Id, stepId, category, message, retryCount);
        _errors.Add(error);
        return error;
    }

    public void Pause()
    {
        if (Status is AgentExecutionStatus.Running)
        {
            Status = AgentExecutionStatus.Paused;
        }
    }

    public void Resume()
    {
        if (Status is AgentExecutionStatus.Paused or AgentExecutionStatus.WaitingForApproval)
        {
            Status = AgentExecutionStatus.Running;
        }
    }

    public void Cancel(string reason)
    {
        Status = AgentExecutionStatus.Cancelled;
        TerminationReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Complete(string? finalOutputText, string? finalOutputJson)
    {
        Status = AgentExecutionStatus.Completed;
        FinalOutputText = finalOutputText;
        FinalOutputJson = finalOutputJson;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = AgentExecutionStatus.Failed;
        TerminationReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void SetUsage(AgentExecutionUsage usage)
    {
        Usage = usage;
    }

    public void SetCost(AgentExecutionCost cost)
    {
        Cost = cost;
    }
}
