using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SaveUserAiPreference;
using AskLucy.Application.Ai.Queries.GetAiModels;
using AskLucy.Application.Ai.Queries.GetEnabledAiProviders;
using AskLucy.Application.Ai.Queries.GetUserAiPreference;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/005-multi-provider-ai-engine — user-facing provider/model catalog
/// (contracts/providers.md) and preferences (contracts/preferences.md). Cacheable reads,
/// not AI-invoking calls, so they sit under the lighter "ai-catalog-endpoints" policy
/// rather than "ai-endpoints" (research.md Decision 6).
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("ai-catalog-endpoints")]
[Route("api/v1/ai")]
public sealed class AiProvidersController(ISender mediator) : ControllerBase
{
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<ProviderSummaryDto>>> GetProviders(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetEnabledAiProvidersQuery(), cancellationToken));

    [HttpGet("providers/{providerId:guid}/models")]
    public async Task<ActionResult<IReadOnlyList<ModelSummaryDto>>> GetModelsForProvider(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAiModelsQuery(providerId), cancellationToken));

    [HttpGet("models")]
    public async Task<ActionResult<IReadOnlyList<ModelSummaryDto>>> GetAllModels(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAiModelsQuery(null), cancellationToken));

    [HttpGet("preferences")]
    public async Task<ActionResult<UserAiPreferenceDto>> GetPreferences(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetUserAiPreferenceQuery(), cancellationToken));

    [HttpPut("preferences")]
    public async Task<ActionResult<UserAiPreferenceDto>> SavePreferences(SaveUserAiPreferenceRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new SaveUserAiPreferenceCommand(request.DefaultProviderId, request.DefaultModelId, request.DefaultGenerationParameters),
            cancellationToken));
}
