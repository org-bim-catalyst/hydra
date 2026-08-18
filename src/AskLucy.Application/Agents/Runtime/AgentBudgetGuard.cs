using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Agents.Runtime;

public enum AgentBudgetLimitType
{
    MaxSteps,
    MaxExecutionDuration,
    MaxTokens,
    MaxCost,
    MaxToolCalls,
    MaxRetries,
}

public sealed record AgentBudgetCheckResult(bool IsExceeded, AgentBudgetLimitType? LimitType, string? Reason)
{
    public static readonly AgentBudgetCheckResult NotExceeded = new(false, null, null);
}

/// <summary>
/// Loop/budget protection (spec.md FR-039/FR-040) — every field on <see
/// cref="AgentExecutionPolicy"/> is optional; a <c>null</c> field falls back to the system-wide
/// default in <see cref="AgentRuntimeOptions"/> rather than being unbounded.
/// </summary>
public sealed class AgentBudgetGuard(IOptions<AgentRuntimeOptions> options)
{
    public AgentBudgetCheckResult Check(
        AgentExecutionPolicy policy,
        DateTime startedAtUtc,
        int stepCount,
        int toolCallCount,
        int retryCount,
        int? inputTokens,
        int? outputTokens,
        decimal? estimatedCost)
    {
        var defaults = options.Value;

        var maxSteps = policy.MaxSteps ?? defaults.DefaultMaxSteps;
        if (stepCount > maxSteps)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxSteps, $"Maximum step count ({maxSteps}) exceeded.");
        }

        var maxDurationSeconds = policy.MaxExecutionDurationSeconds ?? defaults.DefaultMaxExecutionDurationSeconds;
        if ((DateTime.UtcNow - startedAtUtc).TotalSeconds > maxDurationSeconds)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxExecutionDuration, $"Maximum execution duration ({maxDurationSeconds}s) exceeded.");
        }

        var maxTokens = policy.MaxTokens ?? defaults.DefaultMaxTokens;
        if ((inputTokens ?? 0) + (outputTokens ?? 0) > maxTokens)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxTokens, $"Maximum token budget ({maxTokens}) exceeded.");
        }

        var maxCost = policy.MaxCost ?? defaults.DefaultMaxCost;
        if ((estimatedCost ?? 0m) > maxCost)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxCost, $"Maximum cost budget (${maxCost}) exceeded.");
        }

        var maxToolCalls = policy.MaxToolCalls ?? defaults.DefaultMaxToolCalls;
        if (toolCallCount > maxToolCalls)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxToolCalls, $"Maximum tool call count ({maxToolCalls}) exceeded.");
        }

        var maxRetries = policy.MaxRetries ?? defaults.DefaultMaxRetries;
        if (retryCount > maxRetries)
        {
            return new AgentBudgetCheckResult(true, AgentBudgetLimitType.MaxRetries, $"Maximum retry count ({maxRetries}) exceeded.");
        }

        return AgentBudgetCheckResult.NotExceeded;
    }
}
