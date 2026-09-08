using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class ConversationTurnOrchestratorLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site-boundary resolution for chat {UserChatId} exceeded its {BudgetSeconds}s budget; the turn continued without a boundary")]
    public static partial void BoundaryTimedOut(ILogger logger, Guid userChatId, int budgetSeconds);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site-boundary resolution for chat {UserChatId} failed; the turn continued without a boundary")]
    public static partial void BoundaryFailed(ILogger logger, Guid userChatId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Narration for capability {CapabilityKey} in chat {UserChatId} failed; falling back to template wording")]
    public static partial void NarrationFailed(ILogger logger, string capabilityKey, Guid userChatId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Decided slice named capability {CapabilityKey} for chat {UserChatId}, but it was not found in the catalog at dispatch time")]
    public static partial void SliceCapabilityMissingAtDispatch(ILogger logger, string capabilityKey, Guid userChatId);
}

/// <summary>
/// <inheritdoc cref="IConversationTurnOrchestrator"/>
///
/// <para><b>Two paths.</b> The <b>fast path</b> (specs/045 FR-006) is what today's ordinary chat
/// already does: build RAG/memory-augmented context, stream the model's own reply, done. It runs
/// for every turn the decide step judges answerable in words — the large majority of traffic —
/// and costs no more than it did before this feature existed. The <b>act path</b> runs when the
/// decide step names capabilities: it replaces the model's own reply entirely with a sequence of
/// beats — an acknowledgement, then one announce/execute/narrate cycle per capability — because
/// specs/045's whole point is that Lucy's account of what she did should be written from what
/// actually happened, not stitched onto a reply that was already finished.</para>
///
/// <para><b>Provenance.</b> The fast-path body — RAG retrieval, memory retrieval, the standing
/// system-message stack, streaming the reply — is what <c>SendChatMessageCommandHandler</c>'s
/// original 356-line method did for every turn (specs/045 T007-T010 extracted it verbatim). The
/// location-intent classifier, the keyword zoom detector and the automatic boundary trigger that
/// used to run alongside it are retired here (T035-T038): resolving a location, adjusting the
/// viewer and outlining a boundary are now capabilities the decide step chooses explicitly,
/// never side effects of a pipeline stage.</para>
/// </summary>
public sealed class ConversationTurnOrchestrator(
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    IRagService ragService,
    IMemoryService memoryService,
    IUserChatRepository userChatRepository,
    ICurrentUserAccessor currentUser,
    IBackgroundJobClient backgroundJobClient,
    ConversationCapabilityCatalog capabilityCatalog,
    ITurnDecider turnDecider,
    CapabilityExecutor capabilityExecutor,
    ISuggestedActionOfferGenerator offerGenerator,
    ILogger<ConversationTurnOrchestrator> logger) : IConversationTurnOrchestrator
{
    public async IAsyncEnumerable<ChatStreamChunk> RunAsync(
        ConversationTurnRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        var knowledgeBaseIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(request.ChatId, cancellationToken))
            .Select(l => l.KnowledgeBaseId)
            .ToList();

        var userId = currentUser.UserId;
        var chat = await userChatRepository.GetByIdAsync(request.ChatId, cancellationToken);
        var latestUserMessage = request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty;

        var turnContext = BuildTurnContext(userId, request.ChatId, chat, knowledgeBaseIds);

        // The Tier 1 index and the decision are built even on a turn that turns out to need no
        // action — the decider itself is what skips its own model call when the index is empty
        // or the message is blank (FR-006), so an ordinary reply is not charged for asking.
        var index = await capabilityCatalog.BuildIndexAsync(turnContext, latestUserMessage, cancellationToken);
        var decision = await turnDecider.DecideAsync(turnContext, latestUserMessage, index, cancellationToken);

        MemoryRetrievalOutcome? memoryOutcome = null;
        var confirmedLocationThisTurn = false;

        if (decision.IsFastPath)
        {
            // TurnIntent.Suggest takes this same words-only mechanics as Answer (TurnDecision.
            // IsFastPath is true for both) — the distinction that matters for the offer step below
            // is the intent itself, not which mechanics ran (research.md D18).
            await foreach (var chunk in RunFastPathAsync(request, messages, chat, knowledgeBaseIds, cancellationToken))
            {
                if (chunk.MemoryOutcome is not null)
                {
                    memoryOutcome = chunk.MemoryOutcome;
                }

                yield return chunk;
            }
        }
        else
        {
            await foreach (var chunk in RunActPathAsync(request, decision, turnContext, cancellationToken))
            {
                if (chunk.ConfirmedLocation is not null)
                {
                    confirmedLocationThisTurn = true;
                }

                yield return chunk;
            }
        }

        await foreach (var chunk in RunOfferStepAsync(request, decision, turnContext, memoryOutcome, confirmedLocationThisTurn, chat, cancellationToken))
        {
            yield return chunk;
        }

        // spec.md FR-006 (research.md Decision 6) — fire-and-forget background analysis of this
        // turn for new candidate memories, unchanged by which path the turn took.
        backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(request.ChatId, CancellationToken.None));
    }

    /// <summary>
    /// specs/045 US2 — evaluated after the turn's real content is already on its way out, and only
    /// runs the offer step at all when none of FR-025a's five suppression conditions apply. A
    /// suppressed turn costs no model call and yields nothing (SC-008).
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> RunOfferStepAsync(
        ConversationTurnRequest request,
        TurnDecision decision,
        TurnContext turnContext,
        MemoryRetrievalOutcome? memoryOutcome,
        bool confirmedLocationThisTurn,
        Domain.Chats.UserChat? chat,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outcome = new TurnOutcome(
            decision.Intent == TurnIntent.Act ? [.. decision.Slices.Select(s => s.CapabilityKey).Distinct(StringComparer.Ordinal)] : [],
            confirmedLocationThisTurn,
            // FR-025a.4 — "previously offered and ignored" needs the last offer read back from
            // history, which needs a real offer to have existed first. Left at its safe default
            // until specs/045 Phase 5's selection dispatch (tasks.md T068+) gives a persisted
            // offer something to be ignored against — the same documented-placeholder pattern as
            // BuildTurnContext's entitlement rule below.
            [],
            UserDeclinedLastOffer: false);

        // FR-032 placeholder, same pattern as the entitlement rule in BuildTurnContext: no
        // per-user preference exists yet (tasks.md T121 adds the real GET/PUT endpoints and
        // repository). Everyone is treated as opted in, the safe default while there is no toggle
        // to have turned off.
        const bool suggestedActionsEnabled = true;

        var suppression = OfferSuppressionRules.Evaluate(decision.Intent, turnContext, outcome, capabilityCatalog, suggestedActionsEnabled);
        if (suppression != OfferSuppressionReason.None)
        {
            yield break;
        }

        var memoryContext = memoryOutcome?.Type == MemoryRetrievalOutcomeType.Found ? memoryOutcome.ContextText : null;
        if (memoryContext is null && turnContext.UserId is not null)
        {
            // The fast path already retrieves memory for its own reply; the act path does not, so
            // this is the first and only memory call on that path — made only here, after
            // suppression already ruled out the common case of no offer running at all.
            var freshMemory = await memoryService.RetrieveRelevantMemoriesAsync(
                turnContext.UserId, request.ChatId, chat?.ProjectId, request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty,
                cancellationToken);
            if (freshMemory.Type == MemoryRetrievalOutcomeType.Found)
            {
                memoryContext = freshMemory.ContextText;
            }
        }

        var justHappened = DescribeWhatJustHappened(decision, request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty);
        var offer = await offerGenerator.GenerateAsync(turnContext, outcome, justHappened, memoryContext, cancellationToken);

        if (offer is not null)
        {
            yield return new ChatStreamChunk(null, null, SuggestedActions: offer.Actions, SuggestedActionsQuestion: offer.Question);
        }
    }

    /// <summary>A short, factual account of the turn for the offer prompt (FR-021b) — not narration a user reads, only context an LLM composes against.</summary>
    private static string DescribeWhatJustHappened(TurnDecision decision, string userMessage) =>
        decision.Intent switch
        {
            TurnIntent.Act => $"The user asked: \"{userMessage}\". Lucy ran: {string.Join(", ", decision.Slices.Select(s => s.CapabilityKey))}.",
            _ => $"The user asked: \"{userMessage}\". Lucy answered in words; nothing was run.",
        };

    /// <summary>
    /// Today's ordinary chat reply: RAG/memory-augmented context, the standing system-prompt
    /// stack, one streamed completion. Unchanged in shape from before this feature — the whole
    /// point of the fast path is that this is not where anything got more expensive.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> RunFastPathAsync(
        ConversationTurnRequest request,
        List<ChatMessage> messages,
        Domain.Chats.UserChat? chat,
        List<Guid> knowledgeBaseIds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RagRetrievalOutcome? retrievalOutcome = null;
        if (knowledgeBaseIds.Count > 0 && request.Messages.Count > 0)
        {
            retrievalOutcome = await ragService.RetrieveContextAsync(
                request.ChatId, request.Messages[^1].Content, knowledgeBaseIds, cancellationToken);

            if (retrievalOutcome.Type == RagRetrievalOutcomeType.Grounded)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildRagSystemMessage(retrievalOutcome.ContextText!)));
            }
        }

        MemoryRetrievalOutcome? memoryOutcome = null;
        var userId = currentUser.UserId;
        if (userId is not null && request.Messages.Count > 0)
        {
            memoryOutcome = await memoryService.RetrieveRelevantMemoriesAsync(
                userId, request.ChatId, chat?.ProjectId, request.Messages[^1].Content, cancellationToken);

            if (memoryOutcome.Type == MemoryRetrievalOutcomeType.Found)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildMemorySystemMessage(memoryOutcome.ContextText!)));
            }
        }

        // specs/042-site-boundary-resolution research.md #11 — lets the model answer a bare
        // follow-up ("how sure are you about that?") from context alone, with no new capability
        // call. Unaffected by the decide step retiring the automatic boundary trigger: this is
        // about ANSWERING about an already-drawn boundary, not drawing a new one.
        var activeBoundary = chat?.ActiveBoundary;
        if (activeBoundary is not null)
        {
            messages.Insert(0, new ChatMessage(ChatRole.System,
                $"An active site boundary is already shown for '{activeBoundary.SiteName}' " +
                $"(confidence: {activeBoundary.ConfidenceLevel}, source: {activeBoundary.Source}). " +
                "If the user asks about its confidence or source, answer using this information " +
                $"directly — do not claim you cannot access it. {BoundaryConfirmationTemplates.CorrectionGuidance}"));
        }

        // Inserted last and therefore first in the list: every preceding block also uses
        // Insert(0, ...), so adding this earlier would have left it buried under them.
        messages.Insert(0, new ChatMessage(ChatRole.System, ReplyScopePromptFraming.BuildSystemMessage()));

        await foreach (var chunk in request.Provider.StreamChatAsync(messages, request.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new ChatStreamChunk(chunk.ContentDelta, chunk.Usage);
        }

        if (retrievalOutcome is not null || memoryOutcome is not null)
        {
            yield return new ChatStreamChunk(null, null, retrievalOutcome, memoryOutcome);
        }
    }

    /// <summary>
    /// The agentic path (specs/045 FR-001-FR-009): one acknowledgement, then an
    /// announce/execute/narrate cycle per decided slice.
    ///
    /// <para>
    /// Slices run <b>sequentially, in decision order</b>. Genuine dependency wiring — passing one
    /// slice's result into another, running independent slices concurrently — is specs/045's
    /// sub-agent delegation (Phase 7, not yet built); a single-capability turn, which is what
    /// US1's acceptance criteria describe, is unaffected by that simplification. One
    /// acknowledgement covers the whole turn rather than one per slice, since a compound request
    /// naming several capabilities is not yet a designed conversational scenario.
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> RunActPathAsync(
        ConversationTurnRequest request,
        TurnDecision decision,
        TurnContext turnContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var firstCapability = capabilityCatalog.Find(decision.Slices[0].CapabilityKey);
        if (firstCapability is null)
        {
            // The parser already checked this slice's key against the index built moments
            // earlier; finding it gone now means the catalog changed mid-turn (an MCP server
            // deactivating). Degrading to silence would violate constitution §2.VIII, so the
            // turn says plainly that it could not proceed.
            ConversationTurnOrchestratorLog.SliceCapabilityMissingAtDispatch(logger, decision.Slices[0].CapabilityKey, request.ChatId);
            yield return new ChatStreamChunk("I was about to do something, but it's no longer available — could you try again?", null);
            yield break;
        }

        // Beat 1: the acknowledgement. Templated, not model-generated (research.md D15) — it is
        // emitted the instant routing resolves, so the first thing the user sees never depends on
        // a network round trip and is never lost to a narration failure later in the turn.
        yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null);
        yield return new ChatStreamChunk(firstCapability.AcknowledgementTemplate, null);

        foreach (var slice in decision.Slices)
        {
            var capability = capabilityCatalog.Find(slice.CapabilityKey);
            if (capability is null)
            {
                ConversationTurnOrchestratorLog.SliceCapabilityMissingAtDispatch(logger, slice.CapabilityKey, request.ChatId);
                continue;
            }

            // Beat 2: announce-and-work. Opened with a pending label naming the work (FR-005),
            // which stays visible for the whole capability call and is only replaced once the
            // narration below actually has something to say (FR-005a).
            yield return new ChatStreamChunk(null, null, StartsNewMessage: true,
                PendingLabel: slice.PendingLabel ?? capability.Label);

            var result = await capabilityExecutor.ExecuteAsync(capability, turnContext, slice.ArgumentsJson, cancellationToken);

            var narration = await NarrateAsync(request, capability, result, cancellationToken);
            yield return new ChatStreamChunk(narration, null);

            if (result.Succeeded)
            {
                var structured = TryExtractStructuredPayload(capability.Name, result.ResultJson);
                if (structured is not null)
                {
                    // specs/044 FR-001a — flushed as its own chunk immediately, never delayed
                    // behind a later, optional step. There is no automatic boundary chaining
                    // here (FR-046): resolve_site_boundary only runs when the decide step (or,
                    // once Phase 6 ships, a flow) names it explicitly.
                    yield return structured;
                }
            }
        }
    }

    /// <summary>
    /// Turns one capability's real result into the sentence the user reads (FR-007). Falls back
    /// to a fixed, honest template on any failure of the narration call itself (FR-008) — the
    /// user must never be left without a statement of what happened just because the model that
    /// would have phrased it nicely was unavailable.
    /// </summary>
    private async Task<string> NarrateAsync(
        ConversationTurnRequest request,
        IConversationCapability capability,
        CapabilityExecutionResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var narrationMessages = new List<ChatMessage>
            {
                new(ChatRole.System, TurnNarrationPrompt.Build(
                    capability.Label, capability.UsageGuidance, result.Succeeded, result.ResultJson, nextStepLabel: null)),
                new(ChatRole.User, "Report this to the user now."),
            };

            var completion = await request.Provider.ChatAsync(narrationMessages, request.ModelKey, parameters: null, cancellationToken);
            return string.IsNullOrWhiteSpace(completion.Content) ? FallbackNarration(capability, result) : completion.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ConversationTurnOrchestratorLog.NarrationFailed(logger, capability.Name, request.ChatId, ex);
            return FallbackNarration(capability, result);
        }
    }

    /// <summary>FR-008's fixed fallback wording — used only when the narration model call itself fails, never as the normal path.</summary>
    private static string FallbackNarration(IConversationCapability capability, CapabilityExecutionResult result) =>
        result.Succeeded
            ? $"{capability.Label}: done."
            : $"{capability.Label} didn't work — {result.ResultJson}";

    /// <summary>
    /// Recovers the frozen viewer payloads (FR-048) from a capability's own JSON output. Kept as
    /// one small adapter rather than having each capability emit these directly: capabilities
    /// speak <see cref="AgentToolResult"/> JSON so they stay usable by the background agent
    /// runtime too, and only the conversational turn needs to know these specific shapes exist.
    /// </summary>
    private static ChatStreamChunk? TryExtractStructuredPayload(string capabilityKey, string resultJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = document.RootElement;

            switch (capabilityKey)
            {
                case Capabilities.ResolveLocationCapability.CapabilityKey
                    when root.TryGetProperty("locationName", out var nameEl):
                    return new ChatStreamChunk(null, null, ConfirmedLocation: new ConfirmedLocationData(
                        root.GetProperty("latitude").GetDouble(),
                        root.GetProperty("longitude").GetDouble(),
                        nameEl.GetString() ?? string.Empty,
                        root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 1d));

                case Capabilities.ResolveSiteBoundaryCapability.CapabilityKey
                    when root.TryGetProperty("siteName", out var siteEl):
                    // The capability's own JSON carries only the summary fields a narration needs;
                    // the polygon itself lives in ActiveSiteBoundary on the chat aggregate, updated
                    // by IBoundaryResolutionService as a side effect the capability already
                    // triggered. Full ConfirmedSiteBoundaryData reconstruction for the SSE payload
                    // is Phase 6 work (the locate_a_place flow owns this end-to-end); for a
                    // standalone invocation the narration alone still tells the user what happened.
                    _ = siteEl;
                    return null;

                case Capabilities.AdjustViewerFocusCapability.CapabilityKey
                    when root.TryGetProperty("direction", out var directionEl):
                    return new ChatStreamChunk(null, null,
                        ViewerZoom: new ViewerZoomCommand(directionEl.GetString() ?? "in"));

                default:
                    return null;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static TurnContext BuildTurnContext(
        string? userId, Guid userChatId, Domain.Chats.UserChat? chat, IReadOnlyList<Guid> knowledgeBaseIds)
    {
        // specs/045 FR-011 rule 3 — entitlement is meant to be enforced centrally, against a real
        // per-user grant. No granular permission system surfaces to chat today (the pre-existing
        // pipeline ran retrieval/location/memory for any authenticated user unconditionally), so
        // every authenticated user is granted the low-risk permissions the built-in capabilities
        // declare — Low risk is exactly the tier none of them exceed. An unauthenticated turn
        // (userId null) is granted none, which is the safer default. Documented here rather than
        // silently assumed: a real entitlement source is a genuine gap, not an oversight.
        var granted = userId is null
            ? new HashSet<AgentToolPermission>()
            : new HashSet<AgentToolPermission>
            {
                AgentToolPermission.ExternalNetwork,
                AgentToolPermission.ReadKnowledge,
                AgentToolPermission.ReadMemory,
            };

        return new TurnContext(
            userId,
            userChatId,
            chat?.ActiveLocation,
            chat?.ActiveBoundary,
            knowledgeBaseIds,
            HasAttachedDocuments: false,
            IsMemoryAvailable: userId is not null,
            OpenPanelTypeKeys: [],
            granted,
            SubscriptionTier: null);
    }

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
