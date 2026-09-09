using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class CapabilityNarratorLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Narration for capability {CapabilityKey} in chat {UserChatId} failed; falling back to template wording")]
    public static partial void NarrationFailed(ILogger logger, string capabilityKey, Guid userChatId, Exception exception);
}

/// <summary>
/// Turns one capability's real result into the sentence the user reads (FR-007) — shared by every
/// call site that narrates a capability outcome: the single-slice act path, a dispatched
/// selection, a flow step (<see cref="Flows.FlowRunner"/>), and a delegated sub-agent slice (<see
/// cref="SubAgentDelegator"/>). Pulled out as its own type once a fourth, near-identical private
/// copy was about to be written (constitution DRY) — the logic has no dependency on which of
/// those call sites is asking, only on the capability, its outcome, and what (if anything) comes
/// next.
/// <para>
/// Falls back to a fixed, honest template on any failure of the narration call itself (FR-008) —
/// the user must never be left without a statement of what happened just because the model that
/// would have phrased it nicely was unavailable.
/// </para>
/// </summary>
public sealed class CapabilityNarrator(ILogger<CapabilityNarrator> logger)
{
    /// <param name="nextStepLabel">
    /// The next step's own announcement, folded into the same narration call so it can be paired
    /// with this outcome in one message (FR-053). Null for a standalone capability, or when this
    /// outcome failed — a failure's message never continues into what comes next (FR-056).
    /// </param>
    public async Task<string> NarrateAsync(
        ConversationTurnRequest request,
        IConversationCapability capability,
        CapabilityExecutionResult result,
        string? nextStepLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            var narrationMessages = new List<ChatMessage>
            {
                new(ChatRole.System, TurnNarrationPrompt.Build(
                    capability.Label, capability.UsageGuidance, result.Succeeded, result.ResultJson, nextStepLabel)),
                new(ChatRole.User, "Report this to the user now."),
            };

            var completion = await request.Provider.ChatAsync(narrationMessages, request.ModelKey, parameters: null, cancellationToken);
            return string.IsNullOrWhiteSpace(completion.Content) ? FallbackNarration(capability, result, nextStepLabel) : completion.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CapabilityNarratorLog.NarrationFailed(logger, capability.Name, request.ChatId, ex);
            return FallbackNarration(capability, result, nextStepLabel);
        }
    }

    private static string FallbackNarration(IConversationCapability capability, CapabilityExecutionResult result, string? nextStepLabel)
    {
        var text = result.Succeeded ? $"{capability.Label}: done." : $"{capability.Label} didn't work — {result.ResultJson}";
        return nextStepLabel is null || !result.Succeeded ? text : $"{text} {nextStepLabel}";
    }
}
