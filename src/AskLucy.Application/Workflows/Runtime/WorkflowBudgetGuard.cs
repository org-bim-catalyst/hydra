using AskLucy.Application.Options;
using AskLucy.Domain.Workflows;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Workflows.Runtime;

public enum WorkflowBudgetLimitType
{
    MaxNodeCount,
    MaxExecutionDuration,
    MaxTokens,
    MaxCost,
    MaxToolCalls,
    MaxParallelNodes,
    MaxLoopIterations,
}

public sealed record WorkflowBudgetCheckResult(bool IsExceeded, WorkflowBudgetLimitType? LimitType, string? Reason)
{
    public static readonly WorkflowBudgetCheckResult NotExceeded = new(false, null, null);
}

/// <summary>
/// Loop/budget protection (spec.md FR-055/FR-056) — every field on <see
/// cref="WorkflowExecutionPolicy"/> is optional; a <see langword="null"/> field falls back to the
/// system-wide default in <see cref="WorkflowRuntimeOptions"/> rather than being unbounded.
/// Structurally identical to <c>AgentBudgetGuard</c> (research.md Decision 10), extended with two
/// limits Agents has no equivalent for: <see cref="ResolveMaxParallelNodes"/> (sizes the
/// <see cref="System.Threading.SemaphoreSlim"/> a Parallel node's branches run under — enforced
/// structurally by the semaphore itself, so there is no separate "exceeded" check) and
/// <see cref="CheckLoopIteration"/> (FR-032's bounded-loop enforcement).
/// </summary>
public sealed class WorkflowBudgetGuard(IOptions<WorkflowRuntimeOptions> options)
{
    /// <summary>The five running-total checks made once per node, mirroring <c>AgentBudgetGuard.Check</c> exactly.</summary>
    public WorkflowBudgetCheckResult Check(
        WorkflowExecutionPolicy policy,
        DateTime startedAtUtc,
        int nodeCount,
        int toolCallCount,
        int? inputTokens,
        int? outputTokens,
        decimal? estimatedCost)
    {
        var defaults = options.Value;

        var maxNodeCount = policy.MaxNodeCount ?? defaults.DefaultMaxNodeCount;
        if (nodeCount > maxNodeCount)
        {
            return new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxNodeCount, $"Maximum node count ({maxNodeCount}) exceeded.");
        }

        var maxDurationSeconds = policy.MaxExecutionDurationSeconds ?? defaults.DefaultMaxExecutionDurationSeconds;
        if ((DateTime.UtcNow - startedAtUtc).TotalSeconds > maxDurationSeconds)
        {
            return new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxExecutionDuration, $"Maximum execution duration ({maxDurationSeconds}s) exceeded.");
        }

        var maxTokens = policy.MaxTokens ?? defaults.DefaultMaxTokens;
        if ((inputTokens ?? 0) + (outputTokens ?? 0) > maxTokens)
        {
            return new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxTokens, $"Maximum token budget ({maxTokens}) exceeded.");
        }

        var maxCost = policy.MaxCost ?? defaults.DefaultMaxCost;
        if ((estimatedCost ?? 0m) > maxCost)
        {
            return new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxCost, $"Maximum cost budget (${maxCost}) exceeded.");
        }

        var maxToolCalls = policy.MaxToolCalls ?? defaults.DefaultMaxToolCalls;
        if (toolCallCount > maxToolCalls)
        {
            return new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxToolCalls, $"Maximum tool call count ({maxToolCalls}) exceeded.");
        }

        return WorkflowBudgetCheckResult.NotExceeded;
    }

    /// <summary>The concurrency ceiling a Parallel node's branch <see cref="System.Threading.SemaphoreSlim"/> is sized to (research.md Decision 9).</summary>
    public int ResolveMaxParallelNodes(WorkflowExecutionPolicy policy) => policy.MaxParallelNodes ?? options.Value.DefaultMaxParallelNodes;

    /// <summary>FR-041 — a node's own configured <see cref="WorkflowNode.TimeoutSeconds"/>, or the system-wide default per attempt.</summary>
    public int ResolveNodeTimeoutSeconds(int? nodeTimeoutSeconds) => nodeTimeoutSeconds ?? options.Value.DefaultNodeTimeoutSeconds;

    /// <summary>
    /// FR-032 — the effective bound is the LOWER of the loop body's own declared
    /// <c>maxIterations</c> (author-facing, validated present by <c>WorkflowGraphValidator</c>) and
    /// the execution policy's system-wide ceiling, so a workflow author can never author their way
    /// past the platform's own cap.
    /// </summary>
    public WorkflowBudgetCheckResult CheckLoopIteration(WorkflowExecutionPolicy policy, int? nodeDeclaredMaxIterations, int currentIterationCount)
    {
        var policyMax = policy.MaxLoopIterations ?? options.Value.DefaultMaxLoopIterations;
        var effectiveMax = nodeDeclaredMaxIterations is { } declared ? Math.Min(declared, policyMax) : policyMax;

        return currentIterationCount >= effectiveMax
            ? new WorkflowBudgetCheckResult(true, WorkflowBudgetLimitType.MaxLoopIterations, $"Maximum loop iterations ({effectiveMax}) reached.")
            : WorkflowBudgetCheckResult.NotExceeded;
    }
}
