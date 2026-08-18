using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Runtime;

/// <summary><see cref="DependsOnStepIndex"/> is the plan-local index (not a persisted step id yet) of the step this one must wait for (spec.md FR-018) — null means no dependency.</summary>
public sealed record AgentPlanStep(int StepIndex, string Description, AgentExecutionStepType StepType, string? ToolName, int? DependsOnStepIndex = null);

public sealed record AgentPlan(string Goal, IReadOnlyList<AgentPlanStep> Steps);

/// <summary>
/// Produces a task plan for an execution (spec.md FR-012, research.md Decision 11). When the
/// agent has no tools configured, a trivial single-step plan is returned without calling the
/// model — there is nothing to plan between. Otherwise issues one structured AI Provider call
/// (never a provider SDK call directly, FR-032) requesting a JSON plan.
/// </summary>
public interface IAgentPlanner
{
    Task<AgentPlan> CreatePlanAsync(
        string objective,
        AgentInstructions instructions,
        IReadOnlyList<IAgentTool> availableTools,
        string providerKey,
        string modelKey,
        bool modelSupportsJsonMode,
        CancellationToken cancellationToken = default);
}
