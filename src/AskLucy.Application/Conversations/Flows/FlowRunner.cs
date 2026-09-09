using System.Diagnostics;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Flows;

internal static partial class FlowRunnerLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Flow {FlowKey} for chat {UserChatId} stopped at step {StepIndex} ({CapabilityKey}): {Reason}")]
    public static partial void Stopped(ILogger logger, string flowKey, Guid userChatId, int stepIndex, string capabilityKey, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Flow {FlowKey} for chat {UserChatId} exceeded its {BudgetSeconds}s turn budget after step {StepIndex}")]
    public static partial void BudgetExceeded(ILogger logger, string flowKey, Guid userChatId, int budgetSeconds, int stepIndex);
}

/// <summary>
/// Runs a flow's steps in order — dependency-ordered execution, each step's result available to
/// the next, stop-on-failure, skip-when-satisfied (specs/045 FR-055-FR-057, T084).
///
/// <para>
/// <b>Narration reuses the single-capability act path's own mechanism</b>
/// (<see cref="TurnNarrationPrompt"/>), not a second, purely-templated one: contracts/capability-flow.md's
/// own worked example reports step 3's real result ("about 4.2 hectares, medium confidence from
/// OpenStreetMap"), which no static template can produce. <see cref="FlowStep.AnnouncementTemplate"/>
/// is templated (FR-052, research.md D15's acknowledgement precedent, extended to every
/// announcement) — it is said before the work and never depends on an outcome — but each
/// completion is written from what actually happened, with the next step's announcement folded
/// into the same model call via <see cref="TurnNarrationPrompt.Build"/>'s own <c>nextStepLabel</c>
/// parameter, which is exactly what FR-053's pairing needs.
/// </para>
///
/// <para>
/// A step that is already satisfied is the one case with nothing real to narrate (there is no
/// outcome — nothing ran), so its brief note is templated from <see cref="FlowStep.SkipTemplate"/>
/// and manually paired with the next announcement instead.
/// </para>
///
/// <para>
/// <b>Stop-and-name-the-cause-only</b> (FR-056, research.md D19): a failed or timed-out step's
/// message reports only that step; the next announcement is never appended, and the flow ends
/// there. Steps never reached are recorded with a reason but never described to the user — a
/// dependent sequence halting is self-evident, and saying so reads as padding.
/// </para>
/// </summary>
public sealed class FlowRunner(
    ConversationCapabilityCatalog capabilityCatalog,
    CapabilityExecutor capabilityExecutor,
    CapabilityNarrator narrator,
    IOptions<ConversationRuntimeOptions> options,
    ILogger<FlowRunner> logger)
{
    /// <summary>
    /// Runs <paramref name="flow"/> from its first step through <paramref name="throughStepIndex"/>
    /// inclusive, yielding beats exactly like a single-capability act-path slice. Every step run —
    /// including skipped and never-reached ones — is appended to <paramref name="record"/>
    /// (FR-061); an async iterator has no return value of its own to carry this back, so the
    /// caller supplies the list to be populated as a documented side effect, the same idiom
    /// <see cref="ConversationTurnOrchestrator"/> already uses for chunk-derived flags like
    /// "did this turn confirm a location."
    /// </summary>
    public async IAsyncEnumerable<ChatStreamChunk> RunAsync(
        ConversationTurnRequest request,
        IConversationFlow flow,
        int throughStepIndex,
        TurnContext turnContext,
        string flowInputJson,
        List<FlowStepResult> record,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var steps = flow.Steps.Take(throughStepIndex + 1).ToList();
        var completed = new List<FlowStepResult>();
        var stopwatch = Stopwatch.StartNew();
        var turnBudgetSeconds = options.Value.MaxTurnDurationSeconds;

        using var flowInputDocument = JsonDocument.Parse(flowInputJson);

        // Beat 1: the first step's own announcement, standing alone — a flow has no single
        // capability to source a generic acknowledgement from, so the first thing the user reads
        // is what step 1 is about to do (contracts/capability-flow.md §Narration cadence).
        yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: steps[0].AnnouncementTemplate);
        yield return new ChatStreamChunk(steps[0].AnnouncementTemplate, null);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var nextAnnouncement = i + 1 < steps.Count ? steps[i + 1].AnnouncementTemplate : null;

            if (i > 0 && stopwatch.Elapsed.TotalSeconds > turnBudgetSeconds)
            {
                FlowRunnerLog.BudgetExceeded(logger, flow.Key, request.ChatId, turnBudgetSeconds, i);
                for (var j = i; j < steps.Count; j++)
                {
                    completed.Add(new FlowStepResult(steps[j].CapabilityKey, false, false, false, null, "not attempted — the turn's time budget was reached"));
                }

                yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null);
                yield return new ChatStreamChunk("I've stopped there — this was taking longer than expected.", null);
                break;
            }

            var ctx = new FlowStepContext(turnContext, flowInputDocument, completed);

            if (step.IsAlreadySatisfied(ctx))
            {
                completed.Add(new FlowStepResult(step.CapabilityKey, false, true, true, null, "already satisfied"));

                var skipText = step.SkipTemplate ?? "This step was already done, so I've left it as it is.";
                var combined = nextAnnouncement is null ? skipText : $"{skipText} {nextAnnouncement}";

                yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: nextAnnouncement);
                yield return new ChatStreamChunk(combined, null);
                continue;
            }

            var capability = capabilityCatalog.Find(step.CapabilityKey);
            var argumentsDocument = capability is not null ? step.BindArguments(ctx) : null;

            if (capability is null || argumentsDocument is null)
            {
                var reason = capability is null
                    ? $"'{step.CapabilityKey}' is no longer available"
                    : "the information this step needed wasn't available";

                if (!step.IsRequired)
                {
                    completed.Add(new FlowStepResult(step.CapabilityKey, false, false, false, null, reason));
                    continue;
                }

                FlowRunnerLog.Stopped(logger, flow.Key, request.ChatId, i, step.CapabilityKey, reason);
                completed.Add(new FlowStepResult(step.CapabilityKey, false, false, false, null, reason));
                StopForFailure(steps, i, completed);

                yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null);
                yield return new ChatStreamChunk(CapitalizeSentence(reason), null);
                break;
            }

            // Not wrapped in `using`: step 1's own BindArguments returns ctx.FlowInput directly
            // (the same document `flowInputDocument` above owns for the whole run) rather than a
            // freshly allocated one, so disposing whatever comes back here would tear down a
            // document later steps still read from. The handful of short-lived JsonDocuments this
            // produces per flow run are left to the GC rather than risking that aliasing bug.
            var argumentsJson = argumentsDocument.RootElement.GetRawText();
            yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: nextAnnouncement ?? step.AnnouncementTemplate);

            var result = await capabilityExecutor.ExecuteAsync(capability, turnContext, argumentsJson, cancellationToken);
            completed.Add(new FlowStepResult(
                step.CapabilityKey, true, result.Succeeded, false,
                result.Succeeded ? result.ResultJson : null,
                result.Succeeded ? null : result.ResultJson));

            var stepFailed = !result.Succeeded && step.IsRequired;
            var narration = await narrator.NarrateAsync(request, capability, result, stepFailed ? null : nextAnnouncement, cancellationToken);
            yield return new ChatStreamChunk(narration, null);

            if (result.Succeeded)
            {
                var structured = StructuredPayloadExtractor.TryExtract(step.CapabilityKey, result.ResultJson);
                if (structured is not null)
                {
                    yield return structured;
                }
            }

            if (stepFailed)
            {
                FlowRunnerLog.Stopped(logger, flow.Key, request.ChatId, i, step.CapabilityKey, result.ResultJson);
                StopForFailure(steps, i, completed);
                break;
            }

            if (!result.Succeeded)
            {
                // Not required — the flow continues past a failed optional step exactly as it
                // would past a skip; nothing further to report for it.
                continue;
            }
        }

        record.AddRange(completed);
    }

    private static void StopForFailure(List<FlowStep> steps, int failedIndex, List<FlowStepResult> completed)
    {
        for (var j = failedIndex + 1; j < steps.Count; j++)
        {
            completed.Add(new FlowStepResult(steps[j].CapabilityKey, false, false, false, null, "not attempted — an earlier required step failed"));
        }
    }

    private static string CapitalizeSentence(string reason)
    {
        var sentence = char.ToUpperInvariant(reason[0]) + reason[1..];
        return sentence.EndsWith('.') ? sentence : sentence + ".";
    }
}
