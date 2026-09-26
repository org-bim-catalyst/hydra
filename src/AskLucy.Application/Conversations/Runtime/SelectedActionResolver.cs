using System.Text.Json;
using System.Text.Json.Nodes;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// What a selection resolved to: the grounded row itself, plus the id of the message it came from
/// (so the caller can echo <c>offeredByMessageId</c> back and record it on the resulting user
/// message).
/// </summary>
public sealed record ResolvedSelectedAction(SuggestedAction Row, Guid OfferingMessageId);

/// <summary>Resolves and grounds a selected offer row at dispatch time (specs/045 US3, FR-027-FR-029).</summary>
public interface ISelectedActionResolver
{
    /// <exception cref="ConversationActionStaleException">The offer is not in this chat, is not the newest unanswered one, or no longer carries this row (409).</exception>
    /// <exception cref="ConversationActionUnknownException">The row names a capability or flow variant that was never registered (400).</exception>
    /// <exception cref="ConversationActionUnavailableException">The row's capability/flow is registered but no longer available against a freshly built turn context (409).</exception>
    Task<ResolvedSelectedAction> ResolveAsync(
        Guid userChatId, Guid offeredByMessageId, string kind, string? key, string? text, string argumentsJson,
        CancellationToken cancellationToken);
}

/// <summary>
/// <inheritdoc cref="ISelectedActionResolver"/>
///
/// <para>
/// Called from <c>AiController</c> before the user message is persisted — the persisted message's
/// <c>Content</c> is the resolved row's <see cref="SuggestedAction.Label"/> (research.md D6), so
/// resolution has to happen first, not inside <c>SendChatMessageCommandHandler</c> (which only
/// runs once the user message already exists).
/// </para>
///
/// <para>
/// <b>Never trusts the client's own arguments.</b> Once a row is matched, its
/// <see cref="SuggestedAction.ArgumentsJson"/>/<see cref="SuggestedAction.Text"/> — not whatever
/// the request body happened to carry — are what the caller must dispatch. A capability's
/// arguments were already validated once when the offer was grounded (research.md D10); re-running
/// against client-supplied arguments would let a client invoke an offered capability's key with
/// arguments nobody grounded.
/// </para>
/// </summary>
public sealed class SelectedActionResolver(
    IMessageRepository messageRepository,
    IUserChatRepository userChatRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    ICurrentUserAccessor currentUser,
    ConversationCapabilityCatalog capabilityCatalog,
    ConversationFlowCatalog flowCatalog) : ISelectedActionResolver
{
    public async Task<ResolvedSelectedAction> ResolveAsync(
        Guid userChatId, Guid offeredByMessageId, string kind, string? key, string? text, string argumentsJson,
        CancellationToken cancellationToken)
    {
        var parsedKind = ParseKind(kind);

        // Reused for both halves of "newest unanswered offer" — the referenced message and the
        // actual newest offer are both found from the same page of history (data-model.md §2:
        // "no index added... found from the last page of messages already loaded").
        var messages = await messageRepository.ListByChatIdAsync(userChatId, cancellationToken);

        var offering = messages.FirstOrDefault(m => m.Id == offeredByMessageId);
        if (offering is null || offering.UserChatId != userChatId ||
            offering.Role != MessageRole.Assistant || offering.SuggestedActionsJson is null)
        {
            throw new ConversationActionStaleException("That offer is no longer available in this conversation.");
        }

        var newestOffer = messages
            .Where(m => m.Role == MessageRole.Assistant && m.SuggestedActionsJson is not null)
            .OrderBy(m => m.CreatedAtUtc)
            .LastOrDefault();

        if (newestOffer is null || newestOffer.Id != offering.Id)
        {
            throw new ConversationActionStaleException("A newer message has already answered or replaced this offer.");
        }

        var offer = DeserializeOffer(offering.SuggestedActionsJson);
        var matching = offer.Actions.Where(a => a.Kind == parsedKind && RowMatches(a, key, text)).ToList();

        // specs/077 — one offer can carry the same capability more than once with different
        // arguments ("the mall only" / "the mall and its tower" are both set_site_boundary_members),
        // so the key alone no longer says which row was picked. The client's arguments are used
        // only to tell the rows apart; what is dispatched is still the matched row's own.
        var row = matching.Count > 1
            ? matching.FirstOrDefault(a => ArgumentsEqual(a.ArgumentsJson, argumentsJson))
            : matching.FirstOrDefault();
        if (row is null)
        {
            throw new ConversationActionStaleException("That offer no longer contains this option.");
        }

        if (row.IsAction)
        {
            var knowledgeBaseIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(userChatId, cancellationToken))
                .Select(l => l.KnowledgeBaseId)
                .ToList();
            var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
            var context = TurnContextFactory.Build(currentUser.UserId, userChatId, chat?.ActiveLocation, chat?.ActiveBoundary, knowledgeBaseIds);

            if (row.Kind == SuggestedActionKind.FlowVariant)
            {
                // The row's Key is the compound "flowKey:variantKey" (data-model.md §1) the offer
                // step already assembled — never re-derived from the client's own request.
                var separatorIndex = row.Key!.IndexOf(':', StringComparison.Ordinal);
                var flow = separatorIndex > 0 ? flowCatalog.Find(row.Key[..separatorIndex]) : null;
                var variantExists = flow?.Variants.Any(v => v.Key == row.Key[(separatorIndex + 1)..]) ?? false;

                if (flow is null || !variantExists)
                {
                    throw new ConversationActionUnknownException($"'{row.Key}' is not a registered flow variant.");
                }

                if (!flow.IsAvailable(context))
                {
                    throw new ConversationActionUnavailableException($"'{row.Label}' is no longer available.");
                }
            }
            else
            {
                var capability = capabilityCatalog.Find(row.Key!);
                if (capability is null)
                {
                    throw new ConversationActionUnknownException($"'{row.Key}' is not a registered capability.");
                }

                if (!capabilityCatalog.AvailableFor(context).Any(c => string.Equals(c.Name, row.Key, StringComparison.Ordinal)))
                {
                    throw new ConversationActionUnavailableException($"'{row.Label}' is no longer available.");
                }
            }
        }

        return new ResolvedSelectedAction(row, offering.Id);
    }

    private static bool RowMatches(SuggestedAction row, string? key, string? text) => row.Kind switch
    {
        SuggestedActionKind.Capability or SuggestedActionKind.FlowVariant => string.Equals(row.Key, key, StringComparison.Ordinal),
        SuggestedActionKind.FollowUp => string.Equals(row.Text, text, StringComparison.Ordinal),
        SuggestedActionKind.Decline => true,
        _ => false,
    };

    private static bool ArgumentsEqual(string? offered, string selected)
    {
        try
        {
            return JsonNode.DeepEquals(
                string.IsNullOrWhiteSpace(offered) ? null : JsonNode.Parse(offered),
                string.IsNullOrWhiteSpace(selected) ? null : JsonNode.Parse(selected));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static SuggestedActionKind ParseKind(string kind) =>
        SuggestedActionWire.TryParseKind(kind, out var parsed)
            ? parsed
            : throw new ConversationActionUnknownException($"'{kind}' is not a recognised selection kind.");

    // The persisted offer is the wire shape the client was shown, not the domain record: reading
    // it back through anything else is how the two silently stopped describing the same offer.
    private static SuggestedActionOffer DeserializeOffer(string suggestedActionsJson) =>
        SuggestedActionWire.TryDeserialize(suggestedActionsJson)
            ?? throw new ConversationActionStaleException("That offer could not be read.");
}
