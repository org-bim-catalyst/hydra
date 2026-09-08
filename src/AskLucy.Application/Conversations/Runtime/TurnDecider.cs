using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class TurnDeciderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Turn decision for chat {UserChatId}: {Intent} with {SliceCount} slice(s) using {ProviderKey}/{ModelKey} (prompt {PromptVersion})")]
    public static partial void Decided(ILogger logger, Guid userChatId, TurnIntent intent, int sliceCount, string providerKey, string modelKey, string promptVersion);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn decision for chat {UserChatId} returned unreadable content ({Failure}) from {ProviderKey}/{ModelKey}: {ContentPrefix}")]
    public static partial void Unreadable(ILogger logger, Guid userChatId, TurnDecisionParseFailure failure, string providerKey, string modelKey, string contentPrefix);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn decision for chat {UserChatId} dropped a proposed slice: {Reason}")]
    public static partial void SliceDropped(ILogger logger, Guid userChatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn decision for chat {UserChatId} failed; the turn degrades to a plain answer")]
    public static partial void Failed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>Decides what one turn needs (specs/045 FR-001, FR-034, FR-035, FR-037).</summary>
public interface ITurnDecider
{
    Task<TurnDecision> DecideAsync(
        TurnContext context,
        string userMessage,
        IReadOnlyList<CapabilityIndexEntry> index,
        CancellationToken cancellationToken);
}

/// <summary>
/// <inheritdoc cref="ITurnDecider"/>
///
/// <para>
/// One short, structured, non-streaming completion, with a single corrective retry — the idiom
/// <see cref="Agents.Runtime.AgentPlanner"/> already proved on this codebase rather than a second
/// invention. The model is resolved through <see cref="AiCapabilityProviderResolver"/> against
/// <see cref="AiCapability.TurnOrchestration"/>, so which model decides is an administrator's
/// choice and never a hardcoded string (constitution §9).
/// </para>
///
/// <para>
/// <b>Never throws.</b> Every failure — provider outage, unparseable content, an intent nobody
/// recognises — degrades to <see cref="TurnDecision.AnswerOnly"/>. The user still gets an answer;
/// they lose the orchestration for that turn, which is a degradation rather than a failure
/// (FR-039). The acknowledgement they see first is templated, so it is not lost either.
/// </para>
/// </summary>
public sealed class TurnDecider(
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    TurnDecisionParser parser,
    ILogger<TurnDecider> logger) : ITurnDecider
{
    public async Task<TurnDecision> DecideAsync(
        TurnContext context,
        string userMessage,
        IReadOnlyList<CapabilityIndexEntry> index,
        CancellationToken cancellationToken)
    {
        // Nothing to route between, so nothing to ask. Skipping the call here is what keeps an
        // ordinary conversation costing exactly what it costs today (SC-008).
        if (index.Count == 0 || string.IsNullOrWhiteSpace(userMessage))
        {
            return TurnDecision.AnswerOnly;
        }

        var availableKeys = index.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        try
        {
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.TurnOrchestration, cancellationToken);
            var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException("The provider assigned to turn orchestration no longer exists.");
            var model = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
                ?? throw new KeyNotFoundException("The model assigned to turn orchestration no longer exists.");
            var aiProvider = providerResolver.Resolve(provider.ProviderKey);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, TurnDecisionPrompt.Build(index)),
                new(ChatRole.User, userMessage),
            };

            var parameters = new GenerationParametersDto(JsonMode: model.SupportsJsonMode ? true : null);

            var completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters, cancellationToken);
            var result = parser.Parse(completion.Content, availableKeys);

            if (!result.Succeeded)
            {
                // One corrective retry, quoting the parser's own complaint back. A model that
                // fenced its JSON or invented an intent usually fixes it when told precisely
                // what was wrong; a second failure means stop rather than keep paying.
                TurnDeciderLog.Unreadable(logger, context.UserChatId, result.Failure, provider.ProviderKey, model.ModelKey, Truncate(completion.Content));

                messages.Add(new ChatMessage(ChatRole.Assistant, completion.Content));
                messages.Add(new ChatMessage(ChatRole.User,
                    $"That response could not be used ({DescribeFailure(result.Failure)}). Reply again with only the JSON object — no other text."));

                var retry = await aiProvider.ChatAsync(messages, model.ModelKey, parameters, cancellationToken);
                result = parser.Parse(retry.Content, availableKeys);

                if (!result.Succeeded)
                {
                    TurnDeciderLog.Unreadable(logger, context.UserChatId, result.Failure, provider.ProviderKey, model.ModelKey, Truncate(retry.Content));
                    return TurnDecision.AnswerOnly;
                }
            }

            // FR-024 — every discard is recorded with its reason. A model that keeps proposing the
            // same missing capability becomes visible evidence rather than an invisible annoyance.
            foreach (var reason in result.DroppedSliceReasons)
            {
                TurnDeciderLog.SliceDropped(logger, context.UserChatId, reason);
            }

            TurnDeciderLog.Decided(logger, context.UserChatId, result.Decision.Intent, result.Decision.Slices.Count,
                provider.ProviderKey, model.ModelKey, TurnDecisionPrompt.Version);

            return result.Decision;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user cancelled. Propagates so the iterator terminates cleanly, and is never
            // recorded as a decision failure.
            throw;
        }
        catch (Exception ex)
        {
            TurnDeciderLog.Failed(logger, context.UserChatId, ex);
            return TurnDecision.AnswerOnly;
        }
    }

    private static string DescribeFailure(TurnDecisionParseFailure failure) => failure switch
    {
        TurnDecisionParseFailure.NotJson => "it was not a JSON object",
        TurnDecisionParseFailure.UnrecognisedIntent => "the intent was missing or not one of answer/act/suggest",
        TurnDecisionParseFailure.MissingSlices => "intent was act but slices was missing or not an array",
        _ => "it did not match the required shape",
    };

    /// <summary>
    /// A bounded prefix of what actually came back. Without it, "unreadable content" and "the
    /// provider is down" look identical in the log while needing completely different fixes.
    /// </summary>
    private static string Truncate(string content) =>
        string.IsNullOrEmpty(content) ? "(empty)" : content.Length <= 200 ? content : content[..200];
}
