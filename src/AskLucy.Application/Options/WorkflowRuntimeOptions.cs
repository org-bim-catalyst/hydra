using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>Bound from configuration (constitution §4). System-wide defaults a <see cref="Domain.Workflows.WorkflowExecutionPolicy"/> field falls back to when a workflow doesn't override it, and the default per-user concurrency cap (spec.md FR-055/FR-069, research.md Decisions 10-11).</summary>
public sealed class WorkflowRuntimeOptions
{
    public const string SectionName = "Workflows";

    /// <summary>FR-069 — overridden per user via <c>WorkflowUserExecutionLimit</c>.</summary>
    [Range(1, 1000)]
    public int DefaultMaxConcurrentExecutions { get; init; } = 3;

    [Range(1, 10_000)]
    public int DefaultMaxNodeCount { get; init; } = 100;

    [Range(1, 86400)]
    public int DefaultMaxExecutionDurationSeconds { get; init; } = 1800;

    [Range(1, 10_000_000)]
    public int DefaultMaxTokens { get; init; } = 200_000;

    [Range(0.01, 1000)]
    public decimal DefaultMaxCost { get; init; } = 5.00m;

    [Range(1, 200)]
    public int DefaultMaxToolCalls { get; init; } = 50;

    [Range(1, 50)]
    public int DefaultMaxParallelNodes { get; init; } = 10;

    [Range(1, 10_000)]
    public int DefaultMaxLoopIterations { get; init; } = 100;

    [Range(1, 3600)]
    public int DefaultNodeTimeoutSeconds { get; init; } = 300;
}
