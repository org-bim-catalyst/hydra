using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Searches the knowledge bases attached to this conversation (specs/045 FR-013).
///
/// <para>
/// Goes through <see cref="IRagService"/>, the same retrieval pipeline the chat turn already
/// uses — never a second search path. What changes is <i>when</i> it runs: retrieval used to fire
/// unconditionally on every turn with an attached knowledge base, whether or not the question had
/// anything to do with the documents. As a capability it runs when the turn actually calls for it.
/// </para>
///
/// <para>
/// <b>Resolves its own attached knowledge bases</b> via <see cref="IConversationKnowledgeBaseRepository"/>
/// — found live-testing this feature (2026-09-09): the decide step is never shown which knowledge
/// bases are attached to a conversation (that is conversation state, not a Tier 1/Tier 3
/// capability fact), so a schema that <i>required</i> the model to supply their ids as an argument
/// could never be satisfied — the model has no real GUIDs to invent. <see cref="TurnContext.AttachedKnowledgeBaseIds"/>
/// already carries this same list into <see cref="IsAvailable"/>; this capability now reads it the
/// same way <see cref="ResolveSiteBoundaryCapability"/> reads the chat's confirmed location,
/// rather than asking the model to guess something it was never told.
/// </para>
///
/// <para>
/// One of the few capabilities that genuinely <b>is</b> offerable, and the reason
/// <see cref="IsOfferable"/> takes the turn outcome at all: it is worth suggesting once the turn
/// has established a subject to search for, and not worth suggesting when the same search has
/// just run.
/// </para>
/// </summary>
public sealed class SearchKnowledgeBaseCapability(
    IRagService ragService, IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository) : IConversationCapability
{
    public const string CapabilityKey = "search_knowledge_base";

    public string Name => CapabilityKey;

    public string Description =>
        "Searches the knowledge bases attached to this conversation and returns grounded passages with citations.";

    public string WhenToUse =>
        "Use when the user asks what their documents, standards, policies or knowledge bases say " +
        "about something, or when answering well requires material the platform holds rather than " +
        "general knowledge.";

    public string ArgumentHint => "query: what to search for";

    public string UsageGuidance =>
        "Answer from the retrieved passages and cite them. If retrieval found nothing relevant, " +
        "say so rather than filling the gap from general knowledge — a confident answer the " +
        "documents do not support is worse than an admission that they are silent.";

    public string Label => "Search my knowledge bases";

    public string OfferDescription => "Look for this in your attached documents.";

    public string AcknowledgementTemplate => "Let me search your knowledge bases.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadKnowledge];

    public string InputSchemaJson =>
        """{"type":"object","required":["query"],"properties":{"query":{"type":"string","minLength":1}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"outcome":{"type":"string"},"contextText":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Noticeable;

    public SubAgentArea Area => SubAgentArea.Knowledge;

    public bool IsAvailable(TurnContext context) => context.AttachedKnowledgeBaseIds.Count > 0;

    /// <summary>
    /// Offerable once the turn has produced a subject worth searching for. Suppressed when the
    /// search already ran, so an offer never proposes work the user just watched complete.
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) =>
        IsAvailable(context) &&
        !justCompleted.WasInvokedThisTurn(CapabilityKey) &&
        (justCompleted.ConfirmedLocationThisTurn || justCompleted.InvokedCapabilityKeys.Count > 0);

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) ||
            queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty search query is required.");
        }

        var userChatId = context.UserChatId ?? Guid.Empty;
        var attached = await conversationKnowledgeBaseRepository.GetByConversationAsync(userChatId, cancellationToken);
        var knowledgeBaseIds = attached.Select(l => l.KnowledgeBaseId).ToList();

        if (knowledgeBaseIds.Count == 0)
        {
            return AgentToolResult.Failure("No knowledge base is attached to this conversation.");
        }

        try
        {
            var outcome = await ragService.RetrieveContextAsync(userChatId, query, knowledgeBaseIds, cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = outcome.Type.ToString(),
                contextText = outcome.ContextText,
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolResult.Failure($"The knowledge-base search failed: {ex.Message}");
        }
    }
}
