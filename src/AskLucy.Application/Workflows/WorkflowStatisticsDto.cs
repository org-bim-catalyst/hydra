namespace AskLucy.Application.Workflows;

/// <summary>
/// Workflow Monitoring dashboard aggregate (spec.md User Story 8). <see cref="WorkflowExecutionStatus"/>
/// has seven values but spec.md's dashboard only names four buckets — <c>Active</c> folds
/// Running/Paused/WaitingForApproval together (all "in progress, not waiting in the queue"), and
/// <c>Failed</c> folds Failed/Cancelled/TimedOut together (all "ended without a successful output"),
/// a deliberate simplification rather than a fifth/sixth bucket spec.md never asked for.
/// <see cref="FailureRate"/> is computed only over decided/terminal executions (Failed + Completed),
/// excluding still-active or queued ones from the denominator.
/// </summary>
public sealed record WorkflowStatisticsDto(
    int ActiveCount,
    int QueuedCount,
    int FailedCount,
    int CompletedCount,
    double? AverageDurationSeconds,
    double FailureRate,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalEstimatedCost);
