using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

internal static partial class SendChatMessageCommandHandlerLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site-boundary resolution for chat {UserChatId} exceeded its {BudgetSeconds}s budget; the turn continued without a boundary")]
    public static partial void BoundaryTimedOut(ILogger logger, Guid userChatId, int budgetSeconds);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site-boundary resolution for chat {UserChatId} failed; the turn continued without a boundary")]
    public static partial void BoundaryFailed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>
/// MediatR's IPipelineBehavior validation pipeline covers ordinary requests only —
/// stream requests validate inline here (a dedicated stream pipeline behavior would be
/// over-engineering for the single stream request this migration has, per Simplicity/YAGNI).
/// Resolves the provider by key (specs/005-multi-provider-ai-engine, research.md Decision 3)
/// instead of depending on a single injected <see cref="IAIProvider"/> — this is the seam that
/// makes provider switching a configuration/catalog choice, not a code change.
///
/// <para><b>US1 (specs/016-rag-semantic-search, research.md Decision 8)</b>: retrieves context
/// before building the message list, but only when the conversation has one or more attached
/// knowledge bases — a conversation with none attached is completely unaffected (US1 AC2/AC3).
/// <see cref="IRagService"/> never throws; its result rides the final <see cref="ChatStreamChunk"/>
/// so the controller can attach citations to the persisted assistant message and surface a
/// non-silent retrieval-unavailable warning without blocking the chat response (FR-037a).
/// Chat ownership is not re-validated here — the controller already validated it moments earlier
/// via the user-message <c>AppendMessageCommand</c> call that precedes this one.</para>
///
/// <para><b>AI Memory System (specs/018-ai-memory-system, research.md Decisions 2/3/9)</b>:
/// retrieves relevant memories and, when found, inserts their own <c>ChatRole.System</c> message
/// via a second <c>Insert(0, ...)</c> call made *after* RAG's — placing the memory context ahead
/// of RAG's in the final message list (research.md Decision 2). <see cref="IMemoryService"/>
/// never throws (constitution §2.VIII); its outcome rides the final <see cref="ChatStreamChunk"/>
/// exactly like <see cref="IRagService"/>'s, so a memory-subsystem outage degrades the response
/// gracefully rather than blocking it (spec.md FR-014a).</para>
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    IRagService ragService,
    IMemoryService memoryService,
    ILocationResolutionService locationResolutionService,
    IBoundaryResolutionService boundaryResolutionService,
    IViewerZoomDetector viewerZoomDetector,
    IUserChatRepository userChatRepository,
    ICurrentUserAccessor currentUser,
    IBackgroundJobClient backgroundJobClient,
    IOptions<LocationResolutionOptions> locationResolutionOptions,
    IOptions<BoundaryScoringOptions> boundaryScoringOptions,
    ILogger<SendChatMessageCommandHandler> logger,
    IValidator<SendChatMessageCommand> validator) : IStreamRequestHandler<SendChatMessageCommand, ChatStreamChunk>
{
    public async IAsyncEnumerable<ChatStreamChunk> Handle(
        SendChatMessageCommand request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        // The validator already confirmed these exist/are enabled/available — re-fetching
        // here (rather than threading the entities through) keeps the validator's job
        // purely "is this request valid" and the handler's job purely "execute it".
        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);

        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        var knowledgeBaseIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(request.ChatId, cancellationToken))
            .Select(l => l.KnowledgeBaseId)
            .ToList();

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
        var chat = await userChatRepository.GetByIdAsync(request.ChatId, cancellationToken);
        if (userId is not null && request.Messages.Count > 0)
        {
            memoryOutcome = await memoryService.RetrieveRelevantMemoriesAsync(
                userId, request.ChatId, chat?.ProjectId, request.Messages[^1].Content, cancellationToken);

            if (memoryOutcome.Type == MemoryRetrievalOutcomeType.Found)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildMemorySystemMessage(memoryOutcome.ContextText!)));
            }
        }

        // specs/037-location-query-resolution FR-008: launch location resolution concurrently
        // with the model's text stream — never blocking first byte.
        var turnStartUtc = DateTime.UtcNow;
        var activeLocation = chat?.ActiveLocation;
        var latestUserMessage = request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty;
        var locationTask = locationResolutionService.ResolveAsync(
            userId, request.ChatId, latestUserMessage, activeLocation, cancellationToken);

        // specs/038-viewer-poi-zoom US2 T028: detect zoom intent before the AI call so we can
        // inject a guidance system message — the keyword check is synchronous/pure, zero latency.
        var zoomCommand = viewerZoomDetector.Detect(latestUserMessage);
        if (zoomCommand is not null && activeLocation is not null)
        {
            messages.Insert(0, new ChatMessage(ChatRole.System,
                "You are controlling a 3D geospatial viewer. When the user asks you to zoom in " +
                "or out, confirm confidently that you are doing so — never say you are unable to " +
                "zoom or that you cannot control the viewer. The viewer zoom is performed " +
                "automatically; your role is only to provide a natural, brief confirmation."));
        }

        // specs/042-site-boundary-resolution research.md #11: injected before streaming, using
        // the turn-start value of ActiveBoundary, regardless of what this turn's location
        // resolution ends up doing — lets the model answer a bare follow-up ("how sure are you
        // about that?") or a correction request from context alone (FR-009/FR-010), with no new
        // tool call or resolution. Inserting this AFTER streaming starts would have no effect,
        // since StreamChatAsync below already consumes this exact `messages` list.
        var activeBoundary = chat?.ActiveBoundary;
        if (activeBoundary is not null)
        {
            messages.Insert(0, new ChatMessage(ChatRole.System,
                $"An active site boundary is already shown for '{activeBoundary.SiteName}' " +
                $"(confidence: {activeBoundary.ConfidenceLevel}, source: {activeBoundary.Source}). " +
                "If the user asks about its confidence or source, answer using this information " +
                $"directly — do not claim you cannot access it. {BoundaryConfirmationTemplates.CorrectionGuidance}"));
        }

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, model.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new ChatStreamChunk(chunk.ContentDelta, chunk.Usage);
        }

        // Await the location task with the remaining budget from ResolutionCeilingSeconds (FR-013).
        var ceiling = locationResolutionOptions.Value.ResolutionCeilingSeconds;
        var elapsed = DateTime.UtcNow - turnStartUtc;
        var remaining = TimeSpan.FromSeconds(ceiling) - elapsed;
        LocationResolutionOutcome locationOutcome;
        if (remaining <= TimeSpan.Zero && !locationTask.IsCompletedSuccessfully)
        {
            // Budget already elapsed and task hasn't finished — treat as Unavailable immediately.
            locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                LocationConfirmationTemplates.Unavailable);
        }
        else
        {
            try
            {
                locationOutcome = remaining > TimeSpan.Zero
                    ? await locationTask.WaitAsync(remaining, CancellationToken.None)
                    : await locationTask; // Task already completed successfully — retrieve result.
            }
            catch (TimeoutException)
            {
                locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                    LocationConfirmationTemplates.Unavailable);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected — re-throw so the iterator terminates cleanly (I2).
                throw;
            }
            catch (OperationCanceledException)
            {
                // Internal task cancellation (not client disconnect) → Unavailable.
                locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                    LocationConfirmationTemplates.Unavailable);
            }
        }

        // Append the deterministic confirmation/explanation sentence if the intent was non-NoIntent.
        if (locationOutcome.Type != LocationResolutionOutcomeType.NoIntent && locationOutcome.ConfirmationText is not null)
        {
            yield return new ChatStreamChunk(locationOutcome.ConfirmationText, null);
        }

        // specs/038-viewer-poi-zoom US2: reuse the zoom command detected before streaming (T028).
        // C1 fix: only emit ViewerZoom when there is an active location (either confirmed this turn
        // or previously stored on this chat) — prevents zoom-without-location split-brain when the
        // user says "zoom in" with no map context at all.
        var confirmedLocation = locationOutcome.ConfirmedLocation;
        var hasAnyActiveLocation = confirmedLocation is not null || activeLocation is not null;
        var viewerZoom = hasAnyActiveLocation ? zoomCommand : null;

        // specs/044-location-viewer-regression FR-001a: the viewer update is emitted BEFORE the
        // boundary step runs. Between specs/042 and this fix it was emitted after, which made an
        // optional enhancement a hard prerequisite for a mandatory outcome — a boundary failure
        // took __LOCATION__, assistant-message persistence and [DONE] down with it, and a slow one
        // held the viewer for up to ~90s. Restores the pre-88b631a property: no network call sits
        // between resolving a location and delivering it. AiController flushes this chunk's
        // __LOCATION__ event immediately rather than after the stream drains — both halves are
        // required, since the controller's drain-then-write would otherwise nullify this reorder.
        if (retrievalOutcome is not null || memoryOutcome is not null || confirmedLocation is not null || viewerZoom is not null)
        {
            yield return new ChatStreamChunk(null, null, retrievalOutcome, memoryOutcome, confirmedLocation, viewerZoom);
        }

        // specs/042-site-boundary-resolution research.md #11: only resolves a boundary when this
        // turn's confirmed site differs from the one already active — a repeated reference to the
        // same site reuses ActiveBoundary as-is (FR-009), never re-triggering Overpass/scoring.
        // Piggybacks entirely on locationOutcome — no separate intent-classification call.
        if (confirmedLocation is not null &&
            !string.Equals(confirmedLocation.LocationName, activeBoundary?.SiteName, StringComparison.OrdinalIgnoreCase))
        {
            // specs/044 FR-002/FR-003: isolated and bounded (see ResolveBoundarySafelyAsync). The
            // call cannot live inline here — C# forbids `yield return` inside a try/catch — which
            // is precisely why the original code had no protection around it at all.
            var boundaryOutcome = await ResolveBoundarySafelyAsync(confirmedLocation, request.ChatId, cancellationToken);

            if (boundaryOutcome.ConfirmationText is not null)
            {
                yield return new ChatStreamChunk(boundaryOutcome.ConfirmationText, null);
            }

            // specs/044 FR-001b: the boundary is its own later delivery, never bundled with the
            // location chunk above. Omitted entirely when no boundary was produced.
            if (boundaryOutcome.ConfirmedBoundary is not null)
            {
                yield return new ChatStreamChunk(null, null, ConfirmedBoundary: boundaryOutcome.ConfirmedBoundary);
            }
        }

        // spec.md FR-006 (research.md Decision 6) — fire-and-forget background analysis of this
        // turn for new candidate memories. Enqueued against the interface, never the concrete
        // type, so Hangfire resolves it through the container (same idiom DocumentProcessingPipeline
        // already uses); never awaited/blocking, and its own failures never surface here — retried
        // by its own [AutomaticRetry] attribute, with MemoryExtractionSweepJob as the safety net if
        // even the enqueue itself fails.
        backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(request.ChatId, CancellationToken.None));
    }

    /// <summary>
    /// specs/044-location-viewer-regression FR-002/FR-003/FR-007 — runs the optional boundary step
    /// so that neither its failure nor its latency can damage the chat turn.
    /// <para>
    /// <b>Isolation (FR-002).</b> <see cref="IBoundaryResolutionService"/> documents a "never throws"
    /// contract, but turn integrity must not depend on another type keeping its promise: this
    /// catch-all also covers faults outside the vision path the service itself guards (an
    /// unexpected exception type from candidate search, a scoring fault, an empty-collection
    /// index). Per constitution §VIII this is isolation, not suppression — every branch logs its
    /// cause and returns a user-visible outcome.
    /// </para>
    /// <para>
    /// <b>Budget (FR-003).</b> A linked token, not <c>Task.WaitAsync</c>: the latter abandons the
    /// await while Overpass/ESRI/Gemini keep consuming connections on a shared host. Per-dependency
    /// timeouts sum to ~90s, so only an aggregate cap actually bounds the step.
    /// </para>
    /// <para>
    /// <b>Cancellation (FR-007).</b> The two causes are told apart against the ORIGINAL request
    /// token, never the linked one. Once a linked token goes down, <c>GeminiBoundaryVisionAnalyzer</c>'s
    /// own identically-shaped guard sees it as "the caller cancelled" and rethrows on budget expiry;
    /// that is correct only because this method re-adjudicates here. Reversing these two catches
    /// would report every user cancellation as a boundary timeout.
    /// </para>
    /// </summary>
    private async Task<BoundaryResolutionOutcome> ResolveBoundarySafelyAsync(
        ConfirmedLocationData confirmedLocation, Guid userChatId, CancellationToken cancellationToken)
    {
        var budgetSeconds = boundaryScoringOptions.Value.BoundaryTimeoutSeconds;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));

        try
        {
            return await boundaryResolutionService.ResolveAsync(confirmedLocation, userChatId, budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled (client disconnected). A user action, not a boundary failure —
            // it must propagate and must never be recorded as the boundary having failed.
            throw;
        }
        catch (OperationCanceledException)
        {
            SendChatMessageCommandHandlerLog.BoundaryTimedOut(logger, userChatId, budgetSeconds);
            return new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Unavailable, null, BoundaryConfirmationTemplates.Unavailable);
        }
        catch (Exception ex)
        {
            SendChatMessageCommandHandlerLog.BoundaryFailed(logger, userChatId, ex);
            return new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.Unavailable, null, BoundaryConfirmationTemplates.Unavailable);
        }
    }

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
