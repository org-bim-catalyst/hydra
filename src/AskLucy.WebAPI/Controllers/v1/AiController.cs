using AskLucy.Application.Ai.Commands.GenerateImage;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Ai.Commands.Transcribe;
using AskLucy.Application.Ai.Commands.Translate;
using AskLucy.WebAPI.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.WebAPI.Controllers.v1;

/// <summary>
/// FR-001–FR-004: chat/translate/image/transcription, migrated from the legacy
/// unauthenticated <c>ChatGPTController</c>. Every action here requires authentication
/// (FR-015, User Story 2) and is rate-limited (FR-023).
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("ai-endpoints")]
[Route("api/v1/ai")]
public sealed class AiController(ISender mediator) : ControllerBase
{
    [HttpPost("chat")]
    public async Task Chat(ChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var chunk in mediator.CreateStream(new SendChatMessageCommand(request.Messages), cancellationToken))
        {
            await Response.WriteAsync($"data: {chunk}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
    }

    [HttpPost("translate")]
    public async Task<ActionResult<string>> Translate(TranslateRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new TranslateCommand(request.Text, request.TargetLanguage), cancellationToken));

    [HttpPost("images")]
    public async Task<ActionResult<GenerateImageResponse>> GenerateImage(GenerateImageRequest request, CancellationToken cancellationToken)
    {
        var uri = await mediator.Send(new GenerateImageCommand(request.Prompt), cancellationToken);
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
}
