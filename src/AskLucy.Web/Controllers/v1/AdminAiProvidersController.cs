using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Ai.Commands.CheckAiProviderHealth;
using AskLucy.Application.Ai.Commands.ClearAiProviderCredential;
using AskLucy.Application.Ai.Commands.SetAiProviderCredential;
using AskLucy.Application.Ai.Commands.UpdateAiModelStatus;
using AskLucy.Application.Ai.Commands.UpdateAiProvider;
using AskLucy.Application.Ai.Queries.GetAdminAiModels;
using AskLucy.Application.Ai.Queries.GetAdminAiProviders;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/005-multi-provider-ai-engine User Story 1 (FR-001–FR-004) — provider enable/
/// credential administration. specs/008-ai-model-catalog-management adds the model-catalog
/// view/curate/sync actions this controller originally deferred.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/ai")]
public sealed class AdminAiProvidersController(ISender mediator) : ControllerBase
{
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<AdminAiProviderDto>>> GetProviders(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminAiProvidersQuery(), cancellationToken));

    [HttpPatch("providers/{id:guid}")]
    public async Task<IActionResult> UpdateProvider(Guid id, UpdateAiProviderRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateAiProviderCommand(id, request.IsEnabled, request.DefaultModelId), cancellationToken);
        return NoContent();
    }

    [HttpPut("providers/{id:guid}/credential")]
    public async Task<IActionResult> SetCredential(Guid id, SetAiProviderCredentialRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetAiProviderCredentialCommand(id, request.ApiKey), cancellationToken);
        return NoContent();
    }

    [HttpDelete("providers/{id:guid}/credential")]
    public async Task<IActionResult> ClearCredential(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ClearAiProviderCredentialCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/043 FR-024. A provider found failing still returns 200 - the check succeeded, and its finding is the payload; only a failure of the check mechanism is a 5xx.</summary>
    [HttpPost("providers/{providerId:guid}/actions/check-health")]
    [ProducesResponseType<CheckAiProviderHealthResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CheckAiProviderHealthResultDto>> CheckProviderHealth(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CheckAiProviderHealthCommand(providerId), cancellationToken));

    [HttpGet("providers/{providerId:guid}/models")]
    public async Task<ActionResult<IReadOnlyList<AdminAiModelDto>>> GetModels(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminAiModelsQuery(providerId), cancellationToken));

    [HttpPatch("models/{id:guid}")]
    public async Task<IActionResult> UpdateModelStatus(Guid id, UpdateAiModelStatusRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateAiModelStatusCommand(id, request.Status), cancellationToken);
        return NoContent();
    }

    [HttpPost("providers/{providerId:guid}/models/actions/sync")]
    public async Task<ActionResult<ProviderModelSyncDiffDto>> SyncModels(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetProviderModelSyncDiffQuery(providerId), cancellationToken));

    [HttpPost("providers/{providerId:guid}/models/actions/sync/apply")]
    public async Task<ActionResult<ApplyProviderModelSyncResultDto>> ApplyModelSync(Guid providerId, ApplyProviderModelSyncRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ApplyProviderModelSyncCommand(providerId, request.Added, request.RemovedFromVendor), cancellationToken));
}
