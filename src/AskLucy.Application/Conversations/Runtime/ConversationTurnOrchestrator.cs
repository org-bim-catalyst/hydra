using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Conversations;
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
    ConversationFlowCatalog flowCatalog,
    ITurnDecider turnDecider,
    CapabilityExecutor capabilityExecutor,
    FlowRunner flowRunner,
    SubAgentDelegator subAgentDelegator,
    ISuggestedActionOfferGenerator offerGenerator,
    CapabilityNarrator narrator,
    TurnRecorder turnRecorder,
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

        var turnContext = TurnContextFactory.Build(userId, request.ChatId, chat?.ActiveLocation, chat?.ActiveBoundary, knowledgeBaseIds);

        // specs/045 US3 (FR-027) — a selection was already resolved and grounded by
        // ISelectedActionResolver before this command was even dispatched (AiController runs that
        // ahead of persisting the user message, since the message's own Content is the resolved
        // row's label). The decide step is skipped entirely: there is nothing left to decide.
        if (request.SelectedAction is { } selection)
        {
            var selectionConfirmedLocation = false;
            var selectionFlowRecord = new List<FlowStepResult>();

            await foreach (var chunk in RunSelectedActionAsync(request, turnContext, selection, selectionFlowRecord, cancellationToken))
            {
                if (chunk.ConfirmedLocation is not null)
                {
                    selectionConfirmedLocation = true;
                }

                yield return chunk;
            }

            // A dispatched capability may still be worth building on ("what next"); a followUp or
            // decline ran no capability and — for decline especially (FR-025a.3) — is exactly the
            // turn re-offering would be nagging on, so neither runs the offer step at all.
            if (selection.Kind is SuggestedActionKind.Capability or SuggestedActionKind.FlowVariant)
            {
                var invokedKeys = selection.Kind == SuggestedActionKind.Capability
                    ? (IReadOnlyList<string>)[selection.Key!]
                    : [.. selectionFlowRecord.Where(r => r.Attempted).Select(r => r.CapabilityKey).Distinct(StringComparer.Ordinal)];
                var outcome = new TurnOutcome(invokedKeys, selectionConfirmedLocation, [], UserDeclinedLastOffer: false);
                var justHappened = $"The user chose to: {selection.Key}. Lucy ran it.";

                await foreach (var chunk in EmitOfferIfDueAsync(request, TurnIntent.Act, outcome, turnContext, memoryOutcome: null, chat, justHappened, [], cancellationToken))
                {
                    yield return chunk;
                }

                var selectionPlanJson = JsonSerializer.Serialize(new { kind = selection.Kind.ToString(), key = selection.Key });
                await turnRecorder.RecordAsync(
                    request.ChatId, userId, $"selected: {selection.Text ?? selection.Key}", selectionPlanJson,
                    ToRecordedSteps(selectionFlowRecord), justHappened, cancellationToken);
            }

            backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(request.ChatId, CancellationToken.None));
            yield break;
        }

        // The Tier 1 index and the decision are built even on a turn that turns out to need no
        // action — the decider itself is what skips its own model call when the index is empty
        // or the message is blank (FR-006), so an ordinary reply is not charged for asking.
        var index = await capabilityCatalog.BuildIndexAsync(turnContext, latestUserMessage, cancellationToken);
        var flowIndex = flowCatalog.AvailableFor(turnContext).Select(ConversationFlowCatalog.ToEntry).ToList();
        var decision = await turnDecider.DecideAsync(turnContext, latestUserMessage, index, flowIndex, cancellationToken);

        MemoryRetrievalOutcome? memoryOutcome = null;
        var confirmedLocationThisTurn = false;
        var flowRunRecord = new List<FlowStepResult>();
        var sliceRunRecord = new List<SubAgentDelegationResult>();

        if (decision.IsFlowRun)
        {
            var flow = flowCatalog.Find(decision.FlowKey!);
            if (flow is null)
            {
                // The decider already checked this key against the index built moments earlier;
                // finding it gone now means the catalog changed mid-turn. Same graceful
                // degradation as a decided slice whose capability vanished at dispatch time.
                ConversationTurnOrchestratorLog.SliceCapabilityMissingAtDispatch(logger, decision.FlowKey!, request.ChatId);
                yield return new ChatStreamChunk("I was about to do something, but it's no longer available — could you try again?", null);
            }
            else
            {
                var throughStepIndex = Math.Clamp(decision.ThroughStepIndex ?? flow.Steps.Count - 1, 0, flow.Steps.Count - 1);
                await foreach (var chunk in flowRunner.RunAsync(request, flow, throughStepIndex, turnContext, decision.FlowArgumentsJson ?? "{}", flowRunRecord, cancellationToken))
                {
                    if (chunk.ConfirmedLocation is not null)
                    {
                        confirmedLocationThisTurn = true;
                    }

                    yield return chunk;
                }
            }
        }
        else if (decision.IsFastPath)
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
            await foreach (var chunk in RunActPathAsync(request, decision, turnContext, sliceRunRecord, cancellationToken))
            {
                if (chunk.ConfirmedLocation is not null)
                {
                    confirmedLocationThisTurn = true;
                }

                yield return chunk;
            }
        }

        var decideOutcome = new TurnOutcome(
            decision.Intent == TurnIntent.Act
                ? (decision.IsFlowRun
                    ? [.. flowRunRecord.Where(r => r.Attempted).Select(r => r.CapabilityKey).Distinct(StringComparer.Ordinal)]
                    : [.. sliceRunRecord.Select(r => r.CapabilityKey).Distinct(StringComparer.Ordinal)])
                : [],
            confirmedLocationThisTurn,
            // FR-025a.4 — "previously offered and ignored" needs the last offer read back from
            // history, which needs a real offer to have existed first. Left at its safe default:
            // an ordinary (non-selection) turn has no cheap way to know whether the user is
            // ignoring a still-live offer versus there having been none. Selection dispatch above
            // is where Phase 5 gives this a real answer, for the one case that is unambiguous —
            // the exact turn a decline is chosen.
            [],
            UserDeclinedLastOffer: false);

        var decideJustHappened = DescribeWhatJustHappened(decision, latestUserMessage);
        var flowVariantCandidates = FlowVariantCandidatesFor(decision, turnContext);
        await foreach (var chunk in EmitOfferIfDueAsync(request, decision.Intent, decideOutcome, turnContext, memoryOutcome, chat, decideJustHappened, flowVariantCandidates, cancellationToken))
        {
            yield return chunk;
        }

        if (decision.Intent == TurnIntent.Act)
        {
            var recordedSteps = decision.IsFlowRun ? ToRecordedSteps(flowRunRecord) : ToRecordedSteps(sliceRunRecord);
            var decidePlanJson = JsonSerializer.Serialize(new
            {
                intent = decision.Intent.ToString(),
                flowKey = decision.FlowKey,
                slices = decision.Slices.Select(s => s.CapabilityKey),
            });
            await turnRecorder.RecordAsync(request.ChatId, userId, latestUserMessage, decidePlanJson, recordedSteps, decideJustHappened, cancellationToken);
        }

        // spec.md FR-006 (research.md Decision 6) — fire-and-forget background analysis of this
        // turn for new candidate memories, unchanged by which path the turn took.
        backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(request.ChatId, CancellationToken.None));
    }

    /// <summary>FR-038 — a flow's <see cref="FlowStepResult"/> already carries everything <see cref="TurnRecordedStep"/> needs.</summary>
    private static IReadOnlyList<TurnRecordedStep> ToRecordedSteps(IReadOnlyList<FlowStepResult> steps) =>
        [.. steps.Select(s => new TurnRecordedStep(s.CapabilityKey, s.Attempted, s.Succeeded, s.ResultJson, s.Reason))];

    /// <summary>FR-038 — a delegated slice was always attempted (a dropped one never reaches <see cref="SubAgentDelegator"/>'s own record at all); its failure reason is its own result text.</summary>
    private static IReadOnlyList<TurnRecordedStep> ToRecordedSteps(IReadOnlyList<SubAgentDelegationResult> slices) =>
        [.. slices.Select(s => new TurnRecordedStep(s.CapabilityKey, Attempted: true, s.Succeeded, s.ResultJson, s.Succeeded ? null : s.ResultJson))];

    /// <summary>
    /// specs/045 US3 (FR-027) — dispatches an already-resolved, already-grounded selection
    /// directly, with no decide step in between.
    /// <para>
    /// <c>Capability</c> gets the same acknowledge/announce/execute/narrate cadence as an ordinary
    /// act-path slice (FR-051c) — a selected action is not a lesser turn. <c>FollowUp</c> runs no
    /// capability and structurally cannot (FR-021c, SC-002a): it answers as an ordinary reply,
    /// seeded with the follow-up's own composed text rather than the user's literal click.
    /// <c>Decline</c> performs no work at all (FR-022).
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> RunSelectedActionAsync(
        ConversationTurnRequest request,
        TurnContext turnContext,
        SelectedActionInput selection,
        List<FlowStepResult> flowRecord,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (selection.Kind)
        {
            case SuggestedActionKind.Capability:
                var capability = capabilityCatalog.Find(selection.Key!);
                if (capability is null)
                {
                    // ISelectedActionResolver just confirmed this capability was available; finding
                    // it gone now means the catalog changed in the narrow window since (an MCP
                    // server deactivating). Same graceful degradation as a missing decided slice.
                    ConversationTurnOrchestratorLog.SliceCapabilityMissingAtDispatch(logger, selection.Key!, request.ChatId);
                    yield return new ChatStreamChunk("That's no longer available — could you try again?", null);
                    yield break;
                }

                yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null);
                yield return new ChatStreamChunk(capability.AcknowledgementTemplate, null);

                yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: capability.Label);
                var result = await capabilityExecutor.ExecuteAsync(capability, turnContext, selection.ArgumentsJson, cancellationToken);
                var narration = await narrator.NarrateAsync(request, capability, result, nextStepLabel: null, cancellationToken);
                yield return new ChatStreamChunk(narration, null);

                if (result.Succeeded)
                {
                    var structured = StructuredPayloadExtractor.TryExtract(capability.Name, result.ResultJson);
                    if (structured is not null)
                    {
                        yield return structured;
                    }
                }

                // FR-038 — the turn record's own delegation list reuses this same FlowStepResult
                // shape for a single-capability dispatch, exactly as it already does for a
                // FlowVariant dispatch below; RunAsync converts either into a TurnRecordedStep.
                flowRecord.Add(new FlowStepResult(
                    capability.Name, Attempted: true, result.Succeeded, Skipped: false,
                    result.Succeeded ? result.ResultJson : null, result.Succeeded ? null : result.ResultJson));

                break;

            case SuggestedActionKind.FollowUp:
                await foreach (var chunk in RunFollowUpReplyAsync(request, selection.Text ?? string.Empty, cancellationToken))
                {
                    yield return chunk;
                }

                break;

            case SuggestedActionKind.Decline:
                // FR-022/FR-025a.3 — a decline is an answer, not a request for anything further.
                yield return new ChatStreamChunk("Got it — let me know if there's anything else.", null);
                break;

            case SuggestedActionKind.FlowVariant:
                {
                    // The resolved row's Key is the compound "flowKey:variantKey" (data-model.md
                    // §1); ISelectedActionResolver already confirmed both halves exist and the
                    // flow is available, so a missing flow/variant here means the catalog changed
                    // in the narrow window since — same graceful degradation as a vanished
                    // capability.
                    var separatorIndex = selection.Key!.IndexOf(':', StringComparison.Ordinal);
                    var flow = separatorIndex > 0 ? flowCatalog.Find(selection.Key[..separatorIndex]) : null;
                    var variant = flow?.Variants.FirstOrDefault(v => v.Key == selection.Key[(separatorIndex + 1)..]);

                    if (flow is null || variant is null)
                    {
                        ConversationTurnOrchestratorLog.SliceCapabilityMissingAtDispatch(logger, selection.Key, request.ChatId);
                        yield return new ChatStreamChunk("That's no longer available — could you try again?", null);
                        break;
                    }

                    await foreach (var chunk in flowRunner.RunAsync(
                        request, flow, variant.ThroughStepIndex, turnContext, selection.ArgumentsJson, flowRecord, cancellationToken))
                    {
                        yield return chunk;
                    }

                    break;
                }

            default:
                yield return new ChatStreamChunk("That's no longer available — could you try again?", null);
                break;
        }
    }

    /// <summary>
    /// A <c>followUp</c> selection runs no capability and structurally cannot (FR-021c): it is
    /// answered as an ordinary reply — no beats, no acknowledgement — seeded with the follow-up's
    /// own composed text as the thing to respond to, rather than reusing the fast path's retrieval
    /// pipeline (a follow-up is Lucy's own idea, not a fresh question needing RAG/memory lookup).
    /// </summary>
    private static async IAsyncEnumerable<ChatStreamChunk> RunFollowUpReplyAsync(
        ConversationTurnRequest request,
        string followUpText,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        messages.Insert(0, new ChatMessage(ChatRole.System,
            $"The user selected a suggested follow-up: \"{followUpText}\". Respond to it directly and " +
            "naturally, as your next message in the conversation — do not mention that this was a " +
            "suggested option, and do not repeat the option's wording verbatim."));

        await foreach (var chunk in request.Provider.StreamChatAsync(messages, request.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new ChatStreamChunk(chunk.ContentDelta, chunk.Usage);
        }
    }

    /// <summary>
    /// specs/045 US2 — evaluated after the turn's real content is already on its way out, and only
    /// runs the offer step at all when none of FR-025a's five suppression conditions apply. A
    /// suppressed turn costs no model call and yields nothing (SC-008).
    /// <para>
    /// Shared by both callers of the offer step (specs/045 Phase 5, T072): the ordinary decide-based
    /// path builds its <see cref="TurnOutcome"/> from a <see cref="TurnDecision"/>, and a dispatched
    /// selection builds its own from what it just ran — neither needs a different suppression or
    /// composition rule, only a different <paramref name="outcome"/> to evaluate them against.
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> EmitOfferIfDueAsync(
        ConversationTurnRequest request,
        TurnIntent intent,
        TurnOutcome outcome,
        TurnContext turnContext,
        MemoryRetrievalOutcome? memoryOutcome,
        Domain.Chats.UserChat? chat,
        string justHappened,
        IReadOnlyList<FlowVariantOfferCandidate> flowVariantCandidates,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // FR-032 placeholder, same pattern as the entitlement rule in TurnContextFactory: no
        // per-user preference exists yet (tasks.md T121 adds the real GET/PUT endpoints and
        // repository). Everyone is treated as opted in, the safe default while there is no toggle
        // to have turned off.
        const bool suggestedActionsEnabled = true;

        var suppression = OfferSuppressionRules.Evaluate(intent, turnContext, outcome, capabilityCatalog, suggestedActionsEnabled, flowVariantCandidates);
        if (suppression != OfferSuppressionReason.None)
        {
            yield break;
        }

        var memoryContext = memoryOutcome?.Type == MemoryRetrievalOutcomeType.Found ? memoryOutcome.ContextText : null;
        if (memoryContext is null && turnContext.UserId is not null)
        {
            // The fast path already retrieves memory for its own reply; neither the act path nor a
            // dispatched selection does, so this is the first and only memory call for them — made
            // only here, after suppression already ruled out the common case of no offer running
            // at all.
            var freshMemory = await memoryService.RetrieveRelevantMemoriesAsync(
                turnContext.UserId, request.ChatId, chat?.ProjectId, request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty,
                cancellationToken);
            if (freshMemory.Type == MemoryRetrievalOutcomeType.Found)
            {
                memoryContext = freshMemory.ContextText;
            }
        }

        var offer = await offerGenerator.GenerateAsync(turnContext, outcome, justHappened, memoryContext, flowVariantCandidates, cancellationToken);

        if (offer is not null)
        {
            yield return new ChatStreamChunk(null, null, SuggestedActions: offer.Actions, SuggestedActionsQuestion: offer.Question);
        }
    }

    /// <summary>
    /// specs/045 Phase 6 (FR-051a.2, FR-051b) — the flow named by the decide step's own
    /// <see cref="TurnDecision.FlowKey"/>, resolved to its offerable variants with arguments
    /// already bound. Only for <see cref="TurnIntent.Suggest"/>: an <see cref="TurnIntent.Act"/>
    /// run already executed this exact flow moments ago, and offering its variants again would
    /// suggest redoing a job that just finished (FR-025a's spirit, applied to flows specifically).
    /// </summary>
    private IReadOnlyList<FlowVariantOfferCandidate> FlowVariantCandidatesFor(TurnDecision decision, TurnContext turnContext)
    {
        if (decision.FlowKey is null || decision.Intent != TurnIntent.Suggest)
        {
            return [];
        }

        var flow = flowCatalog.Find(decision.FlowKey);
        if (flow is null || !flow.IsAvailable(turnContext))
        {
            return [];
        }

        return ConversationFlowCatalog.VariantCandidatesFor(flow, decision.FlowArgumentsJson ?? "{}");
    }

    /// <summary>A short, factual account of the turn for the offer prompt (FR-021b) — not narration a user reads, only context an LLM composes against.</summary>
    private static string DescribeWhatJustHappened(TurnDecision decision, string userMessage) =>
        decision.Intent switch
        {
            TurnIntent.Act when decision.IsFlowRun => $"The user asked: \"{userMessage}\". Lucy ran the '{decision.FlowKey}' job.",
            TurnIntent.Act => $"The user asked: \"{userMessage}\". Lucy ran: {string.Join(", ", decision.Slices.Select(s => s.CapabilityKey))}.",
            TurnIntent.Suggest when decision.FlowKey is not null => $"The user asked about something related to the '{decision.FlowKey}' job. Lucy answered in words; nothing was run.",
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
    /// The agentic path (specs/045 FR-001-FR-009, Phase 7 FR-016-FR-019): one acknowledgement,
    /// then <see cref="SubAgentDelegator"/> runs every decided slice — independent ones
    /// concurrently, dependent ones in dependency order, each isolated in its own service scope.
    /// One acknowledgement covers the whole turn rather than one per slice, since it is templated
    /// from the first-named slice's own capability (research.md D15) and is meant to open the
    /// turn, not announce each delegation individually — each slice's own pending label already
    /// does that.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> RunActPathAsync(
        ConversationTurnRequest request,
        TurnDecision decision,
        TurnContext turnContext,
        List<SubAgentDelegationResult> sliceRecord,
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

        await foreach (var chunk in subAgentDelegator.RunAsync(request, decision.Slices, turnContext, sliceRecord, cancellationToken))
        {
            yield return chunk;
        }
    }

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
