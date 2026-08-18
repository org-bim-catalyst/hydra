using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4). System-wide defaults an <see cref="Domain.Agents.AgentExecutionPolicy"/> field falls back to when an agent doesn't override it, and the default per-user concurrency cap (spec.md FR-040/FR-042, research.md Decisions 1-2).</summary>
public sealed class AgentRuntimeOptions
{
    public const string SectionName = "Agents";

    /// <summary>FR-042 — overridden per user via <c>AgentUserExecutionLimit</c>.</summary>
    [Range(1, 1000)]
    public int DefaultMaxConcurrentExecutions { get; init; } = 3;

    [Range(1, 1000)]
    public int DefaultMaxSteps { get; init; } = 25;

    [Range(1, 86400)]
    public int DefaultMaxExecutionDurationSeconds { get; init; } = 900;

    [Range(1, 10_000_000)]
    public int DefaultMaxTokens { get; init; } = 200_000;

    [Range(0.01, 1000)]
    public decimal DefaultMaxCost { get; init; } = 5.00m;

    [Range(1, 100)]
    public int DefaultMaxToolCalls { get; init; } = 50;

    [Range(0, 20)]
    public int DefaultMaxRetries { get; init; } = 3;
}
