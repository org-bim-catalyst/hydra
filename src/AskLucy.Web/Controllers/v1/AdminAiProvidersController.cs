using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Ai.Commands.CheckAiProviderHealth;
using AskLucy.Application.Ai.Commands.ClearAiProviderCredential;
using AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;
using AskLucy.Application.Ai.Commands.SetAiProviderCredential;
using AskLucy.Application.Ai.Commands.UpdateAiCapabilitySettings;
using AskLucy.Application.Ai.Commands.UpdateAiModelStatus;
using AskLucy.Application.Ai.Commands.UpdateAiProvider;
using AskLucy.Application.Ai.Queries.GetAdminAiModels;
using AskLucy.Application.Ai.Queries.GetAdminAiProviders;
using AskLucy.Application.Ai.Queries.GetAiCapabilityAssignments;
using AskLucy.Application.Ai.Queries.GetAiCapabilitySettings;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/005-multi-provider-ai-engine User Story 1 (FR-001–FR-004) — provider enable/
/// credential administration. specs/008-ai-model-catalog-management adds the model-catalog
/// view/curate/sync actions this controller originally deferred. Permission-gated (specs/055-
/// role-management research.md Decision 5): reads accept any of the three areas that share this
/// endpoint (AI providers/Default models/AI capabilities); writes require the specific area's
/// manage permission. <see cref="UpdateProvider"/>'s deprecated <c>DefaultModelId</c>/
/// <c>ClearDefaultModel</c> fields are a known interim gap — see that action's own doc comment.
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/ai")]
public sealed class AdminAiProvidersController(ISender mediator) : ControllerBase
{
    [HttpGet("providers")]
    [RequirePermission("admin.ai-providers.view", "admin.default-models.view", "admin.ai-capabilities.view")]
    public async Task<ActionResult<IReadOnlyList<AdminAiProviderDto>>> GetProviders(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminAiProvidersQuery(), cancellationToken));

    /// <summary>
    /// <c>DefaultModelId</c>/<c>ClearDefaultModel</c> are deprecated in favor of
    /// <c>PUT providers/{id}/default-model</c> (contracts/admin-roles-api.md §4), which requires
    /// <c>admin.default-models.manage</c>. This endpoint only requires
    /// <c>admin.ai-providers.manage</c> for all its fields, including the deprecated ones — a
    /// known interim gap (a role with AI-provider-manage but not default-models-manage can still
    /// change the default model through this legacy path) accepted until callers migrate to the
    /// dedicated endpoint.
    /// </summary>
    [HttpPatch("providers/{id:guid}")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<IActionResult> UpdateProvider(Guid id, UpdateAiProviderRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new UpdateAiProviderCommand(id, request.IsEnabled, request.DefaultModelId, request.ClearDefaultModel ?? false),
            cancellationToken);
        return NoContent();
    }

    /// <summary>specs/055-role-management Decision 5 — the dedicated, permission-split successor to <see cref="UpdateProvider"/>'s deprecated default-model fields.</summary>
    [HttpPut("providers/{id:guid}/default-model")]
    [RequirePermission("admin.default-models.manage")]
    public async Task<IActionResult> SetDefaultModel(Guid id, SetProviderDefaultModelRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new UpdateAiProviderCommand(
                ProviderId: id, IsEnabled: null, DefaultModelId: request.DefaultModelId, ClearDefaultModel: request.DefaultModelId is null),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Which provider serves each non-chat capability, and — only where a provider's default
    /// model cannot serve it (image generation) — which of its models. Otherwise the model is
    /// whatever default the assigned provider carries, so the two settings can never disagree.
    /// </summary>
    [HttpGet("capabilities")]
    [RequirePermission("admin.ai-capabilities.view")]
    public async Task<ActionResult<IReadOnlyList<AiCapabilityAssignmentDto>>> GetCapabilityAssignments(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAiCapabilityAssignmentsQuery(), cancellationToken));

    [HttpPut("capabilities/{capability}")]
    [RequirePermission("admin.ai-capabilities.manage")]
    public async Task<IActionResult> SetCapabilityAssignment(
        AiCapability capability, SetAiCapabilityAssignmentRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetAiCapabilityAssignmentCommand(capability, request.ProviderId, request.ModelId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// specs/077 — every capability with the settings it declares (an empty list when it has none),
    /// each carrying its saved value or, when nothing is saved, its default.
    /// </summary>
    [HttpGet("capabilities/settings")]
    [RequirePermission("admin.ai-capabilities.view")]
    public async Task<ActionResult<IReadOnlyList<AiCapabilitySettingsDto>>> GetCapabilitySettings(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAiCapabilitySettingsQuery(), cancellationToken));

    [HttpPut("capabilities/{capability}/settings")]
    [RequirePermission("admin.ai-capabilities.manage")]
    public async Task<IActionResult> UpdateCapabilitySettings(
        AiCapability capability, UpdateAiCapabilitySettingsRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateAiCapabilitySettingsCommand(capability, request.Values), cancellationToken);
        return NoContent();
    }

    [HttpPut("providers/{id:guid}/credential")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<IActionResult> SetCredential(Guid id, SetAiProviderCredentialRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetAiProviderCredentialCommand(id, request.ApiKey), cancellationToken);
        return NoContent();
    }

    [HttpDelete("providers/{id:guid}/credential")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<IActionResult> ClearCredential(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ClearAiProviderCredentialCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>specs/043 FR-024. A provider found failing still returns 200 - the check succeeded, and its finding is the payload; only a failure of the check mechanism is a 5xx.</summary>
    [HttpPost("providers/{providerId:guid}/actions/check-health")]
    [RequirePermission("admin.ai-providers.manage")]
    [ProducesResponseType<CheckAiProviderHealthResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CheckAiProviderHealthResultDto>> CheckProviderHealth(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CheckAiProviderHealthCommand(providerId), cancellationToken));

    [HttpGet("providers/{providerId:guid}/models")]
    [RequirePermission("admin.ai-providers.view", "admin.default-models.view")]
    public async Task<ActionResult<IReadOnlyList<AdminAiModelDto>>> GetModels(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminAiModelsQuery(providerId), cancellationToken));

    [HttpPatch("models/{id:guid}")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<IActionResult> UpdateModelStatus(Guid id, UpdateAiModelStatusRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateAiModelStatusCommand(id, request.Status), cancellationToken);
        return NoContent();
    }

    [HttpPost("providers/{providerId:guid}/models/actions/sync")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<ActionResult<ProviderModelSyncDiffDto>> SyncModels(Guid providerId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetProviderModelSyncDiffQuery(providerId), cancellationToken));

    [HttpPost("providers/{providerId:guid}/models/actions/sync/apply")]
    [RequirePermission("admin.ai-providers.manage")]
    public async Task<ActionResult<ApplyProviderModelSyncResultDto>> ApplyModelSync(Guid providerId, ApplyProviderModelSyncRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ApplyProviderModelSyncCommand(providerId, request.Added, request.RemovedFromVendor), cancellationToken));
}
