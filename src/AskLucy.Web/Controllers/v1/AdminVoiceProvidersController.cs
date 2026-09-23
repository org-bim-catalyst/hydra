using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.AddVoiceProvider;
using AskLucy.Application.Ai.Commands.PreviewVoice;
using AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;
using AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;
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
}
