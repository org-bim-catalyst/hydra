using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// What a retry resolved to: the failed assistant message being retried, and the recorded attempt
/// to replay (data-model.md §4).
///
/// <para>
/// Mirrors <see cref="ResolvedSelectedAction"/>. The attempt is the <b>persisted</b> one, so every
/// parameter of the replay comes from what the server recorded rather than from the request
/// (FR-010).
/// </para>
/// </summary>
public sealed record RetryTarget(Guid SourceMessageId, ActionAttempt Attempt);

/// <summary>Resolves which failed action a retry refers to (specs/068 US2, FR-010, FR-012, FR-015).</summary>
public interface IRetryTargetResolver
{
    /// <param name="failedMessageId">
    /// The assistant message being retried. Null for a typed retry ("try again"), which resolves to
    /// the most recent failure only when that is unambiguous.
    /// </param>
    /// <exception cref="ConversationActionUnknownException">Nothing in this conversation can be retried, or the retry is ambiguous (400).</exception>
    /// <exception cref="ConversationActionStaleException">The named turn succeeded, or its recorded target no longer resolves (409).</exception>
    Task<RetryTarget> ResolveAsync(Guid userChatId, Guid? failedMessageId, CancellationToken cancellationToken);
}

/// <summary>
/// <inheritdoc cref="IRetryTargetResolver"/>
///
/// <para>
/// <b>The client supplies a message id and nothing else.</b> Capability key, arguments and target
/// label all come from the persisted <c>TurnOutcomeJson</c> — the same rule
/// <see cref="SelectedActionResolver"/> follows, and for the same reason: a client that could name
/// the capability and its arguments would be invoking arbitrary capabilities through an endpoint
/// that merely looks like a retry.
/// </para>
///
/// <para>
/// <b>Another user's message is "not found", never "forbidden".</b> A distinct permission error
/// would confirm the id exists, which is exactly the fact a probing client is after
/// (constitution §5).
/// </para>
/// </summary>
public sealed class RetryTargetResolver(
    IMessageRepository messageRepository,
    IUserChatRepository userChatRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    ICurrentUserAccessor currentUser,
    ConversationCapabilityCatalog capabilityCatalog) : IRetryTargetResolver
{
    public async Task<RetryTarget> ResolveAsync(Guid userChatId, Guid? failedMessageId, CancellationToken cancellationToken)
    {
        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat is null || !string.Equals(chat.UserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ConversationActionUnknownException("There is nothing to retry in this conversation.");
        }

        var candidates = (await messageRepository.ListByChatIdAsync(userChatId, cancellationToken))
            .Where(m => m.Role == MessageRole.Assistant && m.TurnOutcomeJson is not null)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => (m.Id, Outcome: TryRead(m.TurnOutcomeJson!)))
            .Where(m => m.Outcome is not null)
            .Select(m => (m.Id, Outcome: m.Outcome!))
            .ToList();

        var (sourceMessageId, attempt) = failedMessageId is { } messageId
            ? Named(candidates, messageId)
            : MostRecent(candidates);

        // The changed-preconditions edge case: the capability that failed has since been
        // deactivated or is no longer available here, so replaying it would fail for a reason the
        // user cannot act on. Said plainly, and as a 409 rather than a silent degradation.
        var capabilityKey = attempt.Key ?? attempt.Kind;
        var knowledgeBaseIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(userChatId, cancellationToken))
            .Select(l => l.KnowledgeBaseId)
            .ToList();
        var context = TurnContextFactory.Build(currentUser.UserId, userChatId, chat.ActiveLocation, chat.ActiveBoundary, knowledgeBaseIds);

        if (capabilityCatalog.Find(capabilityKey) is null)
        {
            throw new ConversationActionUnknownException($"'{capabilityKey}' is no longer a registered capability.");
        }

        if (!capabilityCatalog.AvailableFor(context).Any(c => string.Equals(c.Name, capabilityKey, StringComparison.Ordinal)))
        {
            throw new ConversationActionStaleException(
                $"{attempt.TargetLabel ?? "That action"} can't be retried here any more — it is no longer available in this conversation.");
        }

        return new RetryTarget(sourceMessageId, attempt);
    }

    private static (Guid MessageId, ActionAttempt Attempt) Named(
        IReadOnlyList<(Guid Id, RecordedTurnOutcome Outcome)> candidates, Guid messageId)
    {
        var named = candidates.FirstOrDefault(c => c.Id == messageId);
        if (named.Outcome is null)
        {
            // Covers both "no such message" and "a message in someone else's chat": the chat was
            // already confirmed to be this user's, so anything not in it is simply not found.
            throw new ConversationActionUnknownException("That turn can't be found in this conversation.");
        }

        var failed = named.Outcome.Attempts.FirstOrDefault(a => !a.Succeeded);
        if (failed is null)
        {
            // FR-015 — a turn that worked is never silently run a second time. Telling the user it
            // already succeeded is the answer; doing it again behind their back is not.
            throw new ConversationActionStaleException(
                named.Outcome.Attempts.Count > 0
                    ? "That already succeeded — say the word if you'd like it run again."
                    : "That turn didn't run an action, so there's nothing to retry.");
        }

        return (named.Id, failed);
    }

    private static (Guid MessageId, ActionAttempt Attempt) MostRecent(
        IReadOnlyList<(Guid Id, RecordedTurnOutcome Outcome)> candidates)
    {
        var failures = candidates
            .SelectMany(c => c.Outcome.Attempts.Where(a => !a.Succeeded).Select(a => (c.Id, Attempt: a)))
            .ToList();

        if (failures.Count == 0)
        {
            throw new ConversationActionUnknownException("There's nothing that failed recently for me to retry.");
        }

        // FR-012 — ambiguity is answered with a question, never with a guess. Two failures of the
        // same action on the same target are not ambiguous: retrying either means the same work.
        var newest = failures[^1];
        var distinct = failures
            .Select(f => ((f.Attempt.Key ?? f.Attempt.Kind), f.Attempt.TargetLabel))
            .Distinct()
            .Count();

        return distinct > 1
            ? throw new ConversationActionUnknownException("More than one thing failed recently — which would you like me to try again?")
            : (newest.Id, newest.Attempt);
    }

    private static RecordedTurnOutcome? TryRead(string turnOutcomeJson)
    {
        try
        {
            return JsonSerializer.Deserialize<RecordedTurnOutcome>(turnOutcomeJson, RecordedTurnOutcomeJson.Options);
        }
        catch (JsonException)
        {
            // An unreadable outcome is not a retryable one. ConversationTurnOrchestrator logs this
            // same condition when it builds the routing summary, so it is never silent.
            return null;
        }
    }
}
