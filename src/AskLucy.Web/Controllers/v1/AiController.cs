using System.Text;
using System.Text.RegularExpressions;
using AskLucy.Application.Ai.Commands.GenerateImage;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Ai.Commands.Transcribe;
using AskLucy.Application.Ai.Commands.TranscribeMicrophoneAudio;
using AskLucy.Application.Ai.Commands.Translate;
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
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("ai-endpoints")]
[Route("api/v1/ai")]
public sealed partial class AiController(ISender mediator) : ControllerBase
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
        await foreach (var chunk in mediator.CreateStream(new SendChatMessageCommand(request.Messages), cancellationToken))
        {
            assistantContent.Append(chunk);
            await Response.WriteAsync($"data: {chunk}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);

        await mediator.Send(
            new AppendMessageCommand(request.ChatId, MessageRole.Assistant, MessageKind.Text, assistantContent.ToString(), null),
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

    private static string StripHtml(string html) => TagPattern().Replace(html, string.Empty).Trim();

    [GeneratedRegex("<.*?>")]
    private static partial Regex TagPattern();
}
