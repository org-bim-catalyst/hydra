using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;
using AskLucy.Application.Ai.Commands.GenerateImage;
using AskLucy.Application.Ai.Commands.SaveUserVoicePreference;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Ai.Commands.StreamVoiceReply;
using AskLucy.Application.Ai.Commands.SynthesizeSpeech;
using AskLucy.Application.Ai.Commands.Transcribe;
using AskLucy.Application.Ai.Commands.TranscribeMicrophoneAudio;
using AskLucy.Application.Ai.Commands.Translate;
using AskLucy.Application.Ai.Queries.GetUserVoicePreference;
using AskLucy.Application.Ai.Queries.GetVoiceProviderHealth;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Chats.Commands.RecordActiveLocation;
using AskLucy.Application.Chats.Commands.RecordActiveSiteBoundary;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.Viewer;
using AskLucy.Application.Memory.Commands.RecordMemoryReferences;
using AskLucy.Application.Options;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// FR-001–FR-004: chat/translate/image/transcription, migrated from the legacy
/// unauthenticated <c>ChatGPTController</c>. Every action here requires authentication
/// (FR-015, User Story 2) and is rate-limited (FR-023).
///
/// Each of chat/translate/images also persists its turn as chat history (2026-07-28 decision
/// to add ChatGPT-style conversation history — see AppendMessageCommand's doc comment and
/// spec.md Clarifications). Persistence is composed here, at the controller, rather than
/// added to the AI commands themselves, so SendChatMessageCommand/TranslateCommand/
/// GenerateImageCommand keep their original, already-tested behavior unchanged.
///
/// <c>Chat</c> additionally resolves provider/model attribution and estimated cost
/// (specs/005-multi-provider-ai-engine contracts/chat.md) — it injects
/// <see cref="IAIProviderRepository"/>/<see cref="IAIModelRepository"/> directly (same
/// established convention as <c>UsersController</c> injecting repositories alongside
/// <see cref="ISender"/>) purely to read the display name/pricing needed to attribute and
/// cost the persisted message; the actual generation still goes through
/// <see cref="SendChatMessageCommand"/>/<see cref="IAIProviderResolver"/>.
/// </summary>
internal static partial class AiControllerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Chat turn for chat {ChatId} failed mid-stream after the response had already started; ending the SSE stream cleanly instead of letting the connection drop")]
    public static partial void TurnFailedMidStream(ILogger logger, Exception exception, Guid chatId);
}

[ApiController]
[Authorize]
[EnableRateLimiting("ai-endpoints")]
[Route("api/v1/ai")]
public sealed partial class AiController(
    ISender mediator, IAIProviderRepository providerRepository, IAIModelRepository modelRepository,
    ISelectedActionResolver selectedActionResolver, IOptions<ConversationRuntimeOptions> conversationRuntimeOptions,
    ILogger<AiController> logger) : ControllerBase
{
    [HttpPost("chat")]
    public async Task Chat(ChatRequest request, CancellationToken cancellationToken)
    {
        // specs/045-conversational-agent-runtime US3 (FR-027-FR-029) — resolved BEFORE the user
        // message is persisted, and before Response.ContentType is even set: the message's own
        // Content is the resolved row's label (research.md D6), and a stale/unavailable/unknown
        // selection must come back as a plain Problem Details response, not a started SSE stream.
        // ISelectedActionResolver throws the specific typed exception ProblemDetailsMiddleware
        // maps to 409/400 (T074) — nothing is caught here.
        SelectedActionInput? selectedAction = null;
        string userMessageContent;
        string? persistedSelectedActionKind = null;
        string? persistedSelectedActionKey = null;
        string? persistedSelectedActionArgumentsJson = null;

        if (request.SelectedAction is { } selection)
        {
            var resolved = await selectedActionResolver.ResolveAsync(
                request.ChatId, selection.OfferedByMessageId, selection.Kind, selection.Key, selection.Text,
                selection.Arguments?.GetRawText() ?? "{}", cancellationToken);

            userMessageContent = resolved.Row.Label;
            selectedAction = new SelectedActionInput(
                resolved.OfferingMessageId, resolved.Row.Kind, resolved.Row.Key, resolved.Row.Text, resolved.Row.ArgumentsJson ?? "{}");

            // Message.cs's own invariant: Key/ArgumentsJson are non-null only alongside
            // FlowVariant/Capability. resolved.Row already carries null for both on a FollowUp or
            // Decline row (SuggestedAction's own structural rules), so this mirrors it rather than
            // coercing a "{}" default onto a kind that has no arguments at all.
            persistedSelectedActionKind = resolved.Row.Kind.ToString();
            persistedSelectedActionKey = resolved.Row.Key;
            persistedSelectedActionArgumentsJson = resolved.Row.ArgumentsJson;
        }
        else
        {
            userMessageContent = request.Messages[^1].Content;
        }

        await mediator.Send(
            new AppendMessageCommand(
                request.ChatId, MessageRole.User, MessageKind.Text, userMessageContent, null,
                SelectedActionKind: persistedSelectedActionKind,
                SelectedActionKey: persistedSelectedActionKey,
                SelectedActionArgumentsJson: persistedSelectedActionArgumentsJson),
            cancellationToken);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        // Read before the loop rather than after it: an assistant message can now be persisted
        // mid-stream, when a chunk asks to start a new one.
        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken);
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken);
        var generationParametersJson = request.GenerationParameters is null
            ? null
            : JsonSerializer.Serialize(request.GenerationParameters);

        var assistantContent = new StringBuilder();
        // The turn's first assistant message - the reply. It is the one the memory trace and the
        // __MEMORY__ event refer to; any later message is an application-written confirmation.
        Guid? firstAssistantMessageId = null;
        ChatUsage? finalUsage = null;
        RagRetrievalOutcome? retrievalOutcome = null;
        MemoryRetrievalOutcome? memoryOutcome = null;
        ConfirmedLocationData? confirmedLocation = null;
        ViewerZoomCommand? viewerZoom = null;
        ConfirmedSiteBoundaryData? confirmedBoundary = null;
        ViewerContentCommand? viewerContent = null;
        IReadOnlyList<SuggestedAction>? suggestedActions = null;
        string? suggestedActionsQuestion = null;

        // specs/045-conversational-agent-runtime research.md D5 — a beat can now sit
        // "pending" for tens of seconds (site-boundary resolution). Without a keep-alive, an
        // idle SSE connection on the shared production host risks a proxy buffering or timing
        // it out, which would make the progress indication silently disappear even though the
        // server is still working (SC-004a). WithKeepAliveAsync interleaves a comment line —
        // invisible to aiApi.ts's parser, which only matches "data: " lines — whenever the
        // stream goes quiet for longer than the configured interval.
        try
        {
            await foreach (var chunk in WithKeepAliveAsync(
                mediator.CreateStream(
                    new SendChatMessageCommand(request.ChatId, request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters, selectedAction),
                    cancellationToken),
                TimeSpan.FromSeconds(conversationRuntimeOptions.Value.KeepAliveIntervalSeconds),
                cancellationToken))
            {
                // Handled before this chunk's own content goes out, so the client closes the current
                // bubble and opens a new one ahead of the first character that belongs in it.
                // The break is written even when nothing is buffered — a chunk may open a
                // message purely to say what it is waiting for. Only the persist is conditional.
                if (chunk.StartsNewMessage)
                {
                    if (assistantContent.Length > 0)
                    {
                        firstAssistantMessageId ??= await PersistAssistantMessageAsync(
                            request, assistantContent.ToString(), provider, model, generationParametersJson,
                            finalUsage, retrievalOutcome, null, cancellationToken);
                        assistantContent.Clear();
                    }

                    var breakPayload = chunk.PendingLabel is null
                        ? string.Empty
                        : JsonSerializer.Serialize(new { pendingLabel = chunk.PendingLabel });
                    await Response.WriteAsync($"data: __MESSAGE_BREAK__{breakPayload}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                if (!string.IsNullOrEmpty(chunk.ContentDelta))
                {
                    assistantContent.Append(chunk.ContentDelta);
                    await Response.WriteAsync($"data: {chunk.ContentDelta}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                if (chunk.Usage is not null)
                {
                    finalUsage = chunk.Usage;
                }

                if (chunk.RetrievalOutcome is not null)
                {
                    retrievalOutcome = chunk.RetrievalOutcome;
                }

                if (chunk.MemoryOutcome is not null)
                {
                    memoryOutcome = chunk.MemoryOutcome;
                }

                // specs/044-location-viewer-regression FR-001a: written and flushed HERE, mid-stream,
                // the moment the handler yields it — not after the loop drains. The handler already
                // emits this chunk before starting the optional boundary step, but that reorder alone
                // achieves nothing while this write waits for the whole stream: between specs/042 and
                // this fix, a failing boundary step discarded __LOCATION__ entirely and a slow one held
                // the viewer for up to ~90s. Both halves are required.
                if (chunk.ConfirmedLocation is not null)
                {
                    confirmedLocation = chunk.ConfirmedLocation;
                    await WriteConfirmedLocationEventAsync(request.ChatId, confirmedLocation, cancellationToken);
                }

                if (chunk.ViewerZoom is not null)
                {
                    viewerZoom = chunk.ViewerZoom;
                }

                if (chunk.ViewerContent is not null)
                {
                    viewerContent = chunk.ViewerContent;
                }

                if (chunk.ConfirmedBoundary is not null)
                {
                    confirmedBoundary = chunk.ConfirmedBoundary;
                }

                // specs/045-conversational-agent-runtime FR-021 — rides its own chunk, with no
                // ContentDelta/StartsNewMessage of its own; it never opens a bubble, only marks the
                // offer that belongs to whichever one is open when the turn ends.
                if (chunk.SuggestedActions is not null)
                {
                    suggestedActions = chunk.SuggestedActions;
                    suggestedActionsQuestion = chunk.SuggestedActionsQuestion;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client itself disconnected/cancelled (navigated away, closed the tab, sent a
            // new message) — not a failure to report back to a client that is no longer there.
            return;
        }
        catch (Exception ex)
        {
            // Constitution §2.VIII — an exception here previously propagated uncaught, and since
            // the response had already started (headers sent, text/event-stream in progress),
            // ASP.NET Core cannot turn it into a normal Problem Details response; it just resets
            // the connection. The client's fetch reader then throws a raw network error, which is
            // exactly what surfaced to the user as "Incomplete — connection dropped" for what was
            // often a perfectly ordinary, recoverable failure (a provider hiccup, an external
            // lookup timing out) — never explained, just a severed connection. Ending the stream
            // cleanly here, with whatever partial reply already exists plus a plain explanation,
            // turns that into the same kind of visible, actionable failure every other turn
            // failure mode already gets (TurnDecision.WasDegraded's "I couldn't work out a plan
            // for that" sentence is the sibling of this one).
            AiControllerLog.TurnFailedMidStream(logger, ex, request.ChatId);

            const string failureNotice = " Something went wrong partway through and I couldn't finish. Please try again.";
            await Response.WriteAsync($"data: {failureNotice}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            assistantContent.Append(failureNotice);

            await PersistAssistantMessageAsync(
                request, assistantContent.ToString(), provider, model, generationParametersJson,
                finalUsage, retrievalOutcome, null, cancellationToken);

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            return;
        }

        // US1 (specs/016-rag-semantic-search) — a distinguishable trailing JSON event, never
        // mistakeable for a raw content delta (aiApi.ts's streamChat detects the "__RAG__"
        // prefix before falling back to treating a line as plain content). Surfaces the
        // retrieval outcome/citations/error to the client within the same request, without
        // changing the plain-text wire format every other line already uses.
        if (retrievalOutcome is not null)
        {
            var ragPayload = new
            {
                retrievalOutcome = retrievalOutcome.Type.ToString(),
                citations = retrievalOutcome.Citations.Select(c => new
                {
                    documentChunkId = c.DocumentChunkId,
                    knowledgeBaseId = c.KnowledgeBaseId,
                    documentId = c.DocumentId,
                    documentVersionId = c.DocumentVersionId,
                    documentTitle = c.DocumentTitle,
                    knowledgeBaseName = c.KnowledgeBaseName,
                    pageNumber = c.PageNumber,
                    section = c.Section,
                    excerpt = c.Excerpt,
                }),
                retrievalError = retrievalOutcome.UnavailableReason,
            };
            await Response.WriteAsync($"data: __RAG__{JsonSerializer.Serialize(ragPayload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // Persisted - and, for memory, its trace recorded (FR-014) - before [DONE] is written, so
        // the trailing __MEMORY__ event below can carry the now-real message id and the client can
        // fetch its "why does Lucy know this" trace immediately, in the same session, rather than
        // only after a reload re-fetches persisted history (quickstart.md Scenario 1).
        //
        // Skipped when nothing is buffered, which happens when the turn's last chunk opened a new
        // message that then produced no text: an empty bubble helps nobody.
        Guid? offeringMessageId = null;
        if (assistantContent.Length > 0)
        {
            // specs/045-conversational-agent-runtime FR-026 — the offer, if any, always belongs to
            // this final message: it rides its own trailing chunk with no ContentDelta, so it is
            // never captured mid-stream by the StartsNewMessage branch above.
            // 2026-09-11 live-testing report: this used to serialize the raw SuggestedAction
            // records (PascalCase C# property names — Label, Key, IsDecline...) instead of the
            // camelCase shape the frontend's SuggestedAction type and SuggestedActionCard read
            // (label, capabilityKey, isDecline...) — the exact shape the live __ACTIONS__ event
            // below already builds correctly. Every persisted offer card therefore reopened with
            // every field reading as undefined, rendering the whole card empty (never in the
            // same-session live view, only after a reload re-fetched history). BuildActionWirePayload
            // is now the single source both paths share, so they cannot drift apart again.
            var suggestedActionsJson = suggestedActions is { Count: > 0 }
                ? JsonSerializer.Serialize(new { question = suggestedActionsQuestion, actions = suggestedActions.Select(BuildActionWirePayload) })
                : null;

            var persistedId = await PersistAssistantMessageAsync(
                request, assistantContent.ToString(), provider, model, generationParametersJson,
                finalUsage, retrievalOutcome, suggestedActionsJson, cancellationToken);
            firstAssistantMessageId ??= persistedId;
            offeringMessageId = persistedId;
        }

        // The memory trace belongs to the turn's first message - the reply itself. A later message
        // carries only a confirmation sentence the application wrote, which no memory informed.
        if (memoryOutcome?.Type == MemoryRetrievalOutcomeType.Found && firstAssistantMessageId is { } tracedMessageId)
        {
            await mediator.Send(new RecordMemoryReferencesCommand(tracedMessageId, memoryOutcome.UsedMemories), cancellationToken);
        }

        if (memoryOutcome is not null)
        {
            var memoryPayload = new { messageId = firstAssistantMessageId, memoryOutcome = memoryOutcome.Type.ToString() };
            await Response.WriteAsync($"data: __MEMORY__{JsonSerializer.Serialize(memoryPayload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // specs/042-site-boundary-resolution: resolved site boundary trailing event — same
        // distinguishable-prefix pattern as __LOCATION__. Persisted before the client is told
        // about it (RecordActiveSiteBoundaryCommand), mirroring RecordActiveLocationCommand's
        // ordering exactly, so a client that reloads immediately after sees consistent state.
        if (confirmedBoundary is not null)
        {
            await mediator.Send(new RecordActiveSiteBoundaryCommand(request.ChatId, confirmedBoundary), cancellationToken);

            var boundaryPayload = new
            {
                siteName = confirmedBoundary.SiteName,
                centroid = new { latitude = confirmedBoundary.CentroidLatitude, longitude = confirmedBoundary.CentroidLongitude },
                polygon = confirmedBoundary.Polygon.Select(p => new { latitude = p.Latitude, longitude = p.Longitude }),
                areaSquareMeters = confirmedBoundary.AreaSquareMeters,
                confidence = confirmedBoundary.Confidence,
                confidenceLevel = confirmedBoundary.ConfidenceLevel.ToString().ToLowerInvariant(),
                source = confirmedBoundary.Source.ToString(),
                sourceDetail = confirmedBoundary.SourceDetail,
                alternativeCandidateNames = confirmedBoundary.AlternativeCandidateNames,
            };
            await Response.WriteAsync($"data: __SITE_BOUNDARY__{JsonSerializer.Serialize(boundaryPayload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // specs/038-viewer-poi-zoom US2: explicit zoom command trailing event — emitted when the
        // final chunk carries a ViewerZoomCommand (keyword detected in the user's message).
        if (viewerZoom is not null)
        {
            await Response.WriteAsync($"data: __ZOOM__{viewerZoom.Direction}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // specs/051-viewer-scene-content-api FR-004/research D8 — content Lucy asked the viewer
        // to load, mirroring __ZOOM__'s own trailing-event shape exactly.
        if (viewerContent is not null)
        {
            var viewerContentPayload = new
            {
                fileId = viewerContent.FileId,
                latitude = viewerContent.Latitude,
                longitude = viewerContent.Longitude,
                heightMetres = viewerContent.HeightMetres,
                orientationDegrees = viewerContent.OrientationDegrees,
                scale = viewerContent.Scale,
            };
            await Response.WriteAsync($"data: __VIEWER_CONTENT__{JsonSerializer.Serialize(viewerContentPayload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // specs/045-conversational-agent-runtime FR-021/contracts/turn-stream.md §4 — last before
        // [DONE], and only once the offering message is a real persisted id (an offer with no
        // message to attach to — e.g. the turn's only chunk somehow carried no text — is not
        // emitted at all rather than sent with a fabricated id).
        if (suggestedActions is { Count: > 0 } actions && offeringMessageId is { } offeredByMessageId)
        {
            var actionsPayload = new
            {
                offeredByMessageId,
                question = suggestedActionsQuestion,
                actions = actions.Select(BuildActionWirePayload),
            };
            await Response.WriteAsync($"data: __ACTIONS__{JsonSerializer.Serialize(actionsPayload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
    }

    /// <summary>
    /// The one shape a <see cref="SuggestedAction"/> is put on the wire as — camelCase, and
    /// <c>Key</c> renamed to <c>capabilityKey</c> — shared by the live <c>__ACTIONS__</c> trailing
    /// event and the JSON persisted onto the message for history replay (<c>toOfferFields</c> in
    /// <c>useChatStream.ts</c> parses this same shape back for both). The two must never diverge:
    /// a persisted-only shape drifting from the live one is exactly the bug that made every
    /// reopened offer card render with every field blank (2026-09-11).
    /// </summary>
    private static object BuildActionWirePayload(SuggestedAction a) => new
    {
        kind = a.Kind.ToString(),
        capabilityKey = a.Key,
        text = a.Text,
        label = a.Label,
        description = a.Description,
        arguments = string.IsNullOrEmpty(a.ArgumentsJson) ? (object?)null : JsonSerializer.Deserialize<JsonElement>(a.ArgumentsJson),
        isDecline = a.IsDecline,
    };

    /// <summary>
    /// Persists one assistant message of the current turn, carrying the turn's provider/model
    /// attribution, token usage, estimated cost and RAG citations.
    /// </summary>
    /// <remarks>
    /// A turn can produce more than one assistant message. The site-boundary confirmation reports
    /// a second action that finishes seconds after the location did, so appending it to the reply
    /// ran two unrelated sentences together and silently rewrote a bubble the user had already
    /// read. The handler now marks that chunk as starting a new message, which means persistence
    /// runs once per message rather than once per turn - hence this extraction.
    /// </remarks>
    private async Task<Guid> PersistAssistantMessageAsync(
        ChatRequest request,
        string content,
        AIProvider? provider,
        AIModel? model,
        string? generationParametersJson,
        ChatUsage? usage,
        RagRetrievalOutcome? retrievalOutcome,
        string? suggestedActionsJson,
        CancellationToken cancellationToken)
    {
        var estimatedCostUsd = CostEstimator.Estimate(model?.Pricing, usage?.InputTokenCount, usage?.OutputTokenCount);

        // US1: RAG-grounded citations are attached to the persisted assistant message only when
        // retrieval actually found relevant content - NoRelevantContent/Unavailable never attach
        // citations (research.md Decision 8).
        var citations = retrievalOutcome?.Type == RagRetrievalOutcomeType.Grounded
            ? retrievalOutcome.Citations
                .Select(c => new AppendMessageCitationInput(
                    c.DocumentTitle, null, c.DocumentChunkId, c.KnowledgeBaseId, c.DocumentId, c.DocumentVersionId, c.PageNumber, c.Section))
                .ToList()
            : null;

        var message = await mediator.Send(
            new AppendMessageCommand(
                request.ChatId, MessageRole.Assistant, MessageKind.Text, content, null,
                Provider: provider?.DisplayName, Model: model?.ModelKey, GenerationParametersJson: generationParametersJson,
                InputTokenCount: usage?.InputTokenCount, OutputTokenCount: usage?.OutputTokenCount,
                CachedTokenCount: usage?.CachedTokenCount, ReasoningTokenCount: usage?.ReasoningTokenCount,
                LatencyMs: usage?.LatencyMs, EstimatedCostUsd: estimatedCostUsd, Citations: citations,
                SuggestedActionsJson: suggestedActionsJson),
            cancellationToken);

        return message.Id;
    }

    /// <summary>
    /// specs/036-startup-geolocation US3 / specs/037-location-query-resolution FR-014 /
    /// specs/044-location-viewer-regression FR-001a — persists the confirmed location onto
    /// UserChat (so later back-references resolve without a new geocoding call) and writes the
    /// <c>__LOCATION__</c> trailing event, in that order, so a client reloading immediately after
    /// sees consistent state.
    /// <para>
    /// Extracted from the post-loop block it used to live in so it can be called mid-stream. The
    /// payload shape is unchanged — <c>aiApi.ts</c>'s parser matches on the prefix per line and
    /// has no ordering state, so moving this ahead of <c>__RAG__</c>/<c>__MEMORY__</c> is
    /// invisible to the client.
    /// </para>
    /// </summary>
    /// <summary>
    /// specs/045 research.md D5 — interleaves an SSE comment line whenever <paramref name="source"/>
    /// goes quiet for longer than <paramref name="interval"/>, without altering the sequence or
    /// timing of the real chunks it yields.
    /// <para>
    /// A comment (<c>: keep-alive</c>) rather than a data event: <c>aiApi.ts</c>'s parser splits
    /// on blank-line-terminated SSE records and only interprets lines starting with
    /// <c>data:</c>, so a comment line is invisible to it while still being a byte written to the
    /// wire — which is the only thing that resets an intermediary proxy's idle-connection timer
    /// or defeats response buffering. Racing <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>
    /// against a timer, rather than a fixed-interval loop, means a keep-alive is written only
    /// when nothing else already would have been (a beat's own chunks reset the clock for free).
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> WithKeepAliveAsync(
        IAsyncEnumerable<ChatStreamChunk> source,
        TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        var moveNextTask = enumerator.MoveNextAsync().AsTask();

        while (true)
        {
            var completed = await Task.WhenAny(moveNextTask, Task.Delay(interval, cancellationToken));

            if (completed == moveNextTask)
            {
                if (!await moveNextTask)
                {
                    yield break;
                }

                yield return enumerator.Current;
                moveNextTask = enumerator.MoveNextAsync().AsTask();
            }
            else
            {
                // The delay won the race — the real chunk is still pending. Write the comment and
                // keep waiting on the SAME MoveNextAsync task rather than starting a new one; a
                // fresh await on an in-flight ValueTask-backed operation would be invalid.
                await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
    }

    private async Task WriteConfirmedLocationEventAsync(
        Guid chatId, ConfirmedLocationData confirmedLocation, CancellationToken cancellationToken)
    {
        await mediator.Send(new RecordActiveLocationCommand(chatId, confirmedLocation), cancellationToken);

        var locationPayload = new
        {
            latitude = confirmedLocation.Latitude,
            longitude = confirmedLocation.Longitude,
            locationName = confirmedLocation.LocationName,
            confidence = confirmedLocation.Confidence,
            source = confirmedLocation.Source,
            locationType = confirmedLocation.LocationType,
            viewport = confirmedLocation.Viewport is null ? null : new
            {
                northeastLat = confirmedLocation.Viewport.NortheastLat,
                northeastLng = confirmedLocation.Viewport.NortheastLng,
                southwestLat = confirmedLocation.Viewport.SouthwestLat,
                southwestLng = confirmedLocation.Viewport.SouthwestLng,
            },
        };

        await Response.WriteAsync($"data: __LOCATION__{JsonSerializer.Serialize(locationPayload)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    // [Produces("application/json")]: without it, a bare string ActionResult serializes as
    // text/plain by default, not JSON — silently breaking any JSON-only client like
    // ClientApp/src/api/httpClient.ts's apiFetch. Found live: translating a reply threw
    // "Unexpected token '<'..." trying to JSON-parse the raw HTML response body — this bug
    // predates this session's changes and had been silently breaking Translate since it was
    // first built (T038), since nothing ever surfaced the resulting unhandled rejection.
    [HttpPost("translate")]
    [Produces("application/json")]
    public async Task<ActionResult<string>> Translate(TranslateRequest request, CancellationToken cancellationToken)
    {
        var html = await mediator.Send(new TranslateCommand(request.Text, request.TargetLanguage), cancellationToken);

        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.User, MessageKind.Text, request.Text, null), cancellationToken);
        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.Assistant, MessageKind.Translation, StripHtml(html), request.Text),
            cancellationToken);

        return Ok(html);
    }

    [HttpPost("images")]
    public async Task<ActionResult<GenerateImageResponse>> GenerateImage(GenerateImageRequest request, CancellationToken cancellationToken)
    {
        var uri = await mediator.Send(new GenerateImageCommand(request.Prompt), cancellationToken);

        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.User, MessageKind.Text, request.Prompt, null), cancellationToken);
        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.Assistant, MessageKind.Image, uri.ToString(), request.Prompt),
            cancellationToken);

        return Ok(new GenerateImageResponse(uri.ToString()));
    }

    [HttpPost("transcriptions")]
    public async Task<ActionResult<TranscriptionResponse>> Transcribe(IFormFile file, CancellationToken cancellationToken)
    {
        // specs/034: a missing multipart file part binds IFormFile to null rather than failing
        // model validation, and a present-but-empty file previously sailed through to a real
        // provider call — both are request-input problems, not provider failures, and must be
        // rejected here rather than surfacing as an unclassified/misclassified downstream error
        // (constitution §2.VIII).
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No audio file was provided", Status = StatusCodes.Status400BadRequest });
        }

        await using var stream = file.OpenReadStream();
        var text = await mediator.Send(
            new TranscribeAudioCommand(stream, file.FileName, file.ContentType), cancellationToken);

        return Ok(new TranscriptionResponse(text));
    }

    // Separate from the endpoint above: this expects 16-bit PCM WAV specifically (what the
    // ChatComposer mic recorder produces) and runs through a free, self-hosted Whisper.net
    // model instead of the paid OpenAI API — see ITranscriptionProvider's doc comment.
    [HttpPost("transcriptions/microphone")]
    public async Task<ActionResult<TranscriptionResponse>> TranscribeMicrophone(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No audio file was provided", Status = StatusCodes.Status400BadRequest });
        }

        await using var stream = file.OpenReadStream();
        var text = await mediator.Send(new TranscribeMicrophoneAudioCommand(stream), cancellationToken);

        return Ok(new TranscriptionResponse(text));
    }

    // spec 012-elevenlabs-voice-engine — the full conversational voice engine's HTTP surface.
    // contracts/voice-stt-session.md, voice-preferences.md, voice-provider-health.md,
    // voice-reply-stream.md.
    [HttpPost("voice/stt-session")]
    public async Task<ActionResult<SpeechToTextSession>> CreateSttSession(CreateSpeechToTextSessionRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CreateSpeechToTextSessionCommand(request.Language), cancellationToken));

    [HttpGet("voice/preferences")]
    public async Task<ActionResult<UserVoicePreferenceDto>> GetVoicePreferences(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetUserVoicePreferenceQuery(), cancellationToken));

    [HttpPut("voice/preferences")]
    public async Task<ActionResult<UserVoicePreferenceDto>> SaveVoicePreferences(
        SaveVoicePreferenceRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new SaveUserVoicePreferenceCommand(
                request.ConversationMode, request.IsMuted, request.SelectedVoiceId, request.VoiceSpeed,
                request.VoiceStyle, request.PreferredMicrophoneDeviceId, request.PreferredSpeakerDeviceId,
                request.DefaultLanguage),
            cancellationToken));

    /// <summary>Admin-only aggregate view (contracts/voice-provider-health.md) — same
    /// role-gating convention as <see cref="AdminDashboardController"/>.</summary>
    [HttpGet("voice/health")]
    [Authorize(Policy = "AdministratorOrSuperUser")]
    public async Task<ActionResult<VoiceProviderHealthDto>> GetVoiceProviderHealth(
        DateTime? from, DateTime? to, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetVoiceProviderHealthQuery(from, to), cancellationToken));

    /// <summary>FR-006's "speak every AI reply aloud" — synthesizes speech for text that
    /// already exists (the client's own already-generated chat reply), as opposed to
    /// <see cref="VoiceReply"/>, which generates a new LLM reply and speaks it as it streams.
    /// Never persists a chat message — the caller (ChatPage.tsx) already owns that text.</summary>
    [HttpPost("voice/speak")]
    public async Task Speak(SynthesizeSpeechRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            await foreach (var voiceEvent in mediator.CreateStream(
                new SynthesizeSpeechCommand(request.Text, request.Language), cancellationToken))
            {
                await WriteVoiceEventAsync(voiceEvent, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
        {
            await WriteVoiceErrorAsync(ex, cancellationToken);
        }
    }

    /// <summary>Combines an LLM reply with sentence-by-sentence TTS (research.md, FR-008) —
    /// same persistence responsibility as <see cref="Chat"/>, adapted for the multiplexed
    /// event stream instead of a raw text delta. A stream-level provider failure surfaces as
    /// one client-visible `error` event (constitution §2.VIII) rather than an aborted
    /// connection with no explanation.</summary>
    [HttpPost("voice/reply")]
    public async Task VoiceReply(VoiceReplyRequest request, CancellationToken cancellationToken)
    {
        var lastUserMessage = request.Messages[^1];
        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.User, MessageKind.Text, lastUserMessage.Content, null),
            cancellationToken);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var assistantContent = new StringBuilder();
        ChatUsage? finalUsage = null;

        try
        {
            await foreach (var voiceEvent in mediator.CreateStream(
                new StreamVoiceReplyCommand(request.ChatId, request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters, request.Language),
                cancellationToken))
            {
                if (voiceEvent.TranscriptDelta is not null)
                {
                    assistantContent.Append(voiceEvent.TranscriptDelta);
                }

                if (voiceEvent.Usage is not null)
                {
                    finalUsage = voiceEvent.Usage;
                }

                await WriteVoiceEventAsync(voiceEvent, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
        {
            await WriteVoiceErrorAsync(ex, cancellationToken);
        }

        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken);
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken);
        var estimatedCostUsd = CostEstimator.Estimate(model?.Pricing, finalUsage?.InputTokenCount, finalUsage?.OutputTokenCount);
        var generationParametersJson = request.GenerationParameters is null
            ? null
            : JsonSerializer.Serialize(request.GenerationParameters);

        await mediator.Send(
            new AppendMessageCommand(
                request.ChatId, MessageRole.Assistant, MessageKind.Text, assistantContent.ToString(), null,
                Provider: provider?.DisplayName, Model: model?.ModelKey, GenerationParametersJson: generationParametersJson,
                InputTokenCount: finalUsage?.InputTokenCount, OutputTokenCount: finalUsage?.OutputTokenCount,
                CachedTokenCount: finalUsage?.CachedTokenCount, ReasoningTokenCount: finalUsage?.ReasoningTokenCount,
                LatencyMs: finalUsage?.LatencyMs, EstimatedCostUsd: estimatedCostUsd),
            cancellationToken);
    }

    /// <summary>Maps each <see cref="VoiceReplyEvent"/> onto the exact JSON shape
    /// ClientApp/src/features/chat/api/voiceApi.ts's `VoiceReplyEvent` union expects
    /// (contracts/voice-reply-stream.md) — field names diverge from the C# record's own
    /// (`content` not `TranscriptDelta`, base64 `audio` not raw `AudioBytes`, flattened
    /// `inputTokens`/`outputTokens`/`latencyMs` not a nested usage object).</summary>
    private async Task WriteVoiceEventAsync(VoiceReplyEvent voiceEvent, CancellationToken cancellationToken)
    {
        object payload = voiceEvent.Type switch
        {
            "transcript-delta" => new { type = voiceEvent.Type, content = voiceEvent.TranscriptDelta },
            "audio-chunk" => new
            {
                type = voiceEvent.Type,
                sequence = voiceEvent.AudioSequence,
                audio = Convert.ToBase64String(voiceEvent.AudioBytes!),
            },
            "provider-status" => new { type = voiceEvent.Type, voiceProvider = voiceEvent.VoiceProvider },
            "usage" => new
            {
                type = voiceEvent.Type,
                inputTokens = voiceEvent.Usage?.InputTokenCount,
                outputTokens = voiceEvent.Usage?.OutputTokenCount,
                latencyMs = voiceEvent.Usage?.LatencyMs,
            },
            _ => new { type = voiceEvent.Type },
        };

        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteVoiceErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
        var payload = new
        {
            type = "error",
            errorType = ex.GetType().Name,
            title = "The voice reply failed.",
            detail = ex.Message,
        };
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static string StripHtml(string html) => TagPattern().Replace(html, string.Empty).Trim();

    [GeneratedRegex("<.*?>")]
    private static partial Regex TagPattern();
}
