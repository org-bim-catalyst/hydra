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
    public bool IsDuplicate(IEnumerable<AgentToolCall> priorToolCalls, string toolName, string validatedInputJson) =>
        priorToolCalls.Any(c =>
            c.ValidatedOutputJson is not null &&
            c.ToolName == toolName &&
            string.Equals(c.ValidatedInputJson, validatedInputJson, StringComparison.Ordinal));
}
