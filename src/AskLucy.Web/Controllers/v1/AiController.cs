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

        await foreach (var chunk in mediator.CreateStream(
            new SendChatMessageCommand(request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters),
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
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);

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
                request.VoiceStyle, request.PreferredMicrophoneDeviceId, request.PreferredSpeakerDeviceId),
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
                new StreamVoiceReplyCommand(request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters, request.Language),
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
