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
using AskLucy.Application.Locations;
using AskLucy.Application.Memory.Commands.RecordMemoryReferences;
using AskLucy.Domain.Chats;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
[ApiController]
[Authorize]
[EnableRateLimiting("ai-endpoints")]
[Route("api/v1/ai")]
public sealed partial class AiController(
    ISender mediator, IAIProviderRepository providerRepository, IAIModelRepository modelRepository) : ControllerBase
{
    [HttpPost("chat")]
    public async Task Chat(ChatRequest request, CancellationToken cancellationToken)
    {
        var lastUserMessage = request.Messages[^1];
        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.User, MessageKind.Text, lastUserMessage.Content, null),
            cancellationToken);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var assistantContent = new StringBuilder();
        ChatUsage? finalUsage = null;
        RagRetrievalOutcome? retrievalOutcome = null;
        MemoryRetrievalOutcome? memoryOutcome = null;
        ConfirmedLocationData? confirmedLocation = null;
        ViewerZoomCommand? viewerZoom = null;
        ConfirmedSiteBoundaryData? confirmedBoundary = null;

        await foreach (var chunk in mediator.CreateStream(
            new SendChatMessageCommand(request.ChatId, request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters),
            cancellationToken))
        {
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

            if (chunk.ConfirmedBoundary is not null)
            {
                confirmedBoundary = chunk.ConfirmedBoundary;
            }
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

        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken);
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken);
        var estimatedCostUsd = CostEstimator.Estimate(model?.Pricing, finalUsage?.InputTokenCount, finalUsage?.OutputTokenCount);
        var generationParametersJson = request.GenerationParameters is null
            ? null
            : JsonSerializer.Serialize(request.GenerationParameters);

        // US1: RAG-grounded citations are attached to the persisted assistant message only when
        // retrieval actually found relevant content — NoRelevantContent/Unavailable never attach
        // citations (research.md Decision 8).
        var citations = retrievalOutcome?.Type == RagRetrievalOutcomeType.Grounded
            ? retrievalOutcome.Citations
                .Select(c => new AppendMessageCitationInput(
                    c.DocumentTitle, null, c.DocumentChunkId, c.KnowledgeBaseId, c.DocumentId, c.DocumentVersionId, c.PageNumber, c.Section))
                .ToList()
            : null;

        // Persisted — and, for memory, its trace recorded (FR-014) — before [DONE] is written, so
        // the trailing __MEMORY__ event below can carry the now-real message id and the client can
        // fetch its "why does Lucy know this" trace immediately, in the same session, rather than
        // only after a reload re-fetches persisted history (quickstart.md Scenario 1).
        var assistantMessage = await mediator.Send(
            new AppendMessageCommand(
                request.ChatId, MessageRole.Assistant, MessageKind.Text, assistantContent.ToString(), null,
                Provider: provider?.DisplayName, Model: model?.ModelKey, GenerationParametersJson: generationParametersJson,
                InputTokenCount: finalUsage?.InputTokenCount, OutputTokenCount: finalUsage?.OutputTokenCount,
                CachedTokenCount: finalUsage?.CachedTokenCount, ReasoningTokenCount: finalUsage?.ReasoningTokenCount,
                LatencyMs: finalUsage?.LatencyMs, EstimatedCostUsd: estimatedCostUsd, Citations: citations),
            cancellationToken);

        if (memoryOutcome?.Type == MemoryRetrievalOutcomeType.Found)
        {
            await mediator.Send(new RecordMemoryReferencesCommand(assistantMessage.Id, memoryOutcome.UsedMemories), cancellationToken);
        }

        if (memoryOutcome is not null)
        {
            var memoryPayload = new { messageId = assistantMessage.Id, memoryOutcome = memoryOutcome.Type.ToString() };
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

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
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
