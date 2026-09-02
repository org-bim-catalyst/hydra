using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Runtime;

/// <summary>
/// Loop protection (spec.md FR-039): halts an execution that repeats an exact
/// <c>(ToolName, ValidatedInputJson)</c> pair it has already completed successfully — a repeat of
/// a previously *failed* call is left to the normal retry/backoff budget (FR-037/<see
/// cref="AgentBudgetGuard"/>) instead, since that is a legitimate recovery attempt, not a loop.
/// </summary>
public sealed class AgentDuplicateToolCallDetector
{
    // CA1822: kept as an instance member rather than static — this is a DI-registered service
    // consumed as an instance dependency (AgentExecutionOrchestrator's constructor) and via an
    // instance reference in AskLucy.Application.Tests, outside this cleanup's Application/Domain
    // scope; making it static would be a breaking signature/call-site change there.
#pragma warning disable CA1822
    public bool IsDuplicate(IEnumerable<AgentToolCall> priorToolCalls, string toolName, string validatedInputJson) =>
#pragma warning restore CA1822
        priorToolCalls.Any(c =>
            c.ValidatedOutputJson is not null &&
            c.ToolName == toolName &&
            string.Equals(c.ValidatedInputJson, validatedInputJson, StringComparison.Ordinal));
}
