using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.AddVoiceProvider;
using AskLucy.Application.Ai.Commands.PreviewVoice;
using AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;
using AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;
using AskLucy.Application.Ai.Dictation.Commands.SelectLocalWhisperModel;
using AskLucy.Application.Ai.Dictation.Commands.SetDictationPrimaryEngine;
using AskLucy.Application.Ai.Dictation.Commands.SetPushToTalkEngine;
using AskLucy.Application.Ai.Dictation.Commands.TryLocalWhisperModel;
using AskLucy.Application.Ai.Dictation.Queries.GetDictationSettings;
using AskLucy.Application.Ai.Queries.GetAdminVoiceProviders;
using AskLucy.Application.Ai.Queries.GetVoiceEngines;
using AskLucy.Application.Ai.Queries.GetVoiceProviderVoices;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/070 contracts/admin-voice.md — Lucy's text-to-speech providers: which engines are
/// added, their credentials, the voices each offers, which one is Lucy's primary voice, and an
/// audition endpoint that speaks a sample sentence. Shares the AI-provider permissions, since a
/// voice provider is an AI provider credential like any other.
/// <para>specs/078 contracts/admin-dictation.md adds the dictation engine and Local Whisper model routes.</para>
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/voice")]
public sealed class AdminVoiceProvidersController(ISender mediator) : ControllerBase
{
    /// <summary>Every engine the platform can speak through, flagged with whether it has been added yet.</summary>
    [HttpGet("engines")]
    [RequirePermission("admin.ai-providers.view")]
    public async Task<ActionResult<IReadOnlyList<VoiceEngineDto>>> GetEngines(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetVoiceEnginesQuery(), cancellationToken));

    [HttpGet("providers")]
    [RequirePermission("admin.ai-providers.view")]
    public async Task<ActionResult<IReadOnlyList<AdminVoiceProviderDto>>> GetProviders(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminVoiceProvidersQuery(), cancellationToken));

    [HttpPost("providers")]
    [RequirePermission("admin.ai-providers.manage")]
    [ProducesResponseType<AdminVoiceProviderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminVoiceProviderDto>> AddProvider(AddVoiceProviderRequest request, CancellationToken cancellationToken)
    {
        var provider = await mediator.Send(new AddVoiceProviderCommand(request.ProviderKey, request.ApiKey), cancellationToken);
        return CreatedAtAction(nameof(GetProviders), null, provider);
    }

    [HttpPut("providers/{id:guid}/credential")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<ActionResult<AdminVoiceProviderDto>> SetCredential(Guid id, SetAiProviderCredentialRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetVoiceProviderCredentialCommand(id, request.ApiKey), cancellationToken));

    /// <summary>Asks the provider for its voices live, so a voice added on the vendor's side appears without a sync step.</summary>
    [HttpGet("providers/{id:guid}/voices")]
    [RequirePermission("admin.ai-providers.view")]
    public async Task<ActionResult<IReadOnlyList<VoiceOptionDto>>> GetVoices(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetVoiceProviderVoicesQuery(id), cancellationToken));

    /// <summary>Makes the provider Lucy's voice; the others keep their order behind it as failovers.</summary>
    [HttpPut("primary")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<ActionResult<IReadOnlyList<AdminVoiceProviderDto>>> SetPrimary(SetPrimaryVoiceProviderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetPrimaryVoiceProviderCommand(request.ProviderId, request.VoiceId), cancellationToken));

    /// <summary>Speaks a sample sentence with the chosen voice. Manage permission, because it spends the provider's quota.</summary>
    [HttpPost("providers/{id:guid}/preview")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<ActionResult<VoicePreviewDto>> Preview(Guid id, PreviewVoiceRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PreviewVoiceCommand(id, request.VoiceId, request.Text, request.Language), cancellationToken));

    /// <summary>specs/078 — the dictation engines, the Local Whisper model, and which choices are selectable.</summary>
    [HttpGet("dictation")]
    [RequirePermission("admin.ai-providers.view")]
    public async Task<ActionResult<DictationSettingsDto>> GetDictation(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetDictationSettingsQuery(), cancellationToken));

    /// <summary>specs/078 FR-009a — selects the Local Whisper model; it doesn't change the primary engine.</summary>
    [HttpPut("dictation/local-whisper-model")]
    [RequirePermission("admin.ai-providers.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SelectLocalWhisperModel(SelectLocalWhisperModelRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SelectLocalWhisperModelCommand(request.CustomModelId, request.RowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/078 FR-004 — sets the primary dictation engine; OpenAI/ElevenLabs are only selectable while their vendor is switched on.</summary>
    [HttpPut("dictation/primary")]
    [RequirePermission("admin.ai-providers.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetDictationPrimaryEngine(SetDictationPrimaryEngineRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetDictationPrimaryEngineCommand(request.Engine, request.RowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/078 FR-017 — sets the Push-to-Talk engine used under ElevenLabs realtime.</summary>
    [HttpPut("dictation/push-to-talk")]
    [RequirePermission("admin.ai-providers.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetPushToTalkEngine(SetPushToTalkEngineRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetPushToTalkEngineCommand(request.Engine, request.RowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/078 FR-009c — transcribes a recorded sample on a deployment without selecting it.</summary>
    [HttpPost("dictation/try")]
    [RequirePermission("admin.ai-providers.manage")]
    [RequestSizeLimit(TryRequestSizeLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = TryRequestSizeLimitBytes)]
    [ProducesResponseType<LocalWhisperTryResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LocalWhisperTryResultDto>> TryLocalWhisperModel(
        IFormFile file, [FromForm] Guid customModelId, [FromForm] string? language, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No audio file was provided", Status = StatusCodes.Status400BadRequest });
        }

        await using var stream = file.OpenReadStream();
        return Ok(await mediator.Send(new TryLocalWhisperModelCommand(customModelId, stream, language), cancellationToken));
    }

    private const long TryRequestSizeLimitBytes = 4 * 1024 * 1024;
}
