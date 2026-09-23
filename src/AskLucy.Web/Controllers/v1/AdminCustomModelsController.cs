using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Commands.CancelCustomModelDeployment;
using AskLucy.Application.CustomModels.Commands.RemoveCustomModel;
using AskLucy.Application.CustomModels.Commands.SetCustomModelAvailability;
using AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;
using AskLucy.Application.CustomModels.Queries.GetCustomModel;
using AskLucy.Application.CustomModels.Queries.GetDeploymentStatus;
using AskLucy.Application.CustomModels.Queries.ListCustomModels;
using AskLucy.Application.CustomModels.Queries.PreviewCustomModelSource;
using AskLucy.Application.Users;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/072 contracts/admin-custom-models.md — server-side deployments of Hugging Face model
/// repositories to the production host. Nothing here returns the deployment target's host,
/// username, password or root path (FR-017, FR-018).
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/custom-models")]
public sealed class AdminCustomModelsController(ISender mediator) : ControllerBase
{
    /// <summary>Newest first.</summary>
    [HttpGet]
    [RequirePermission("admin.custom-models.view")]
    public async Task<ActionResult<PagedResult<CustomModelSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ListCustomModelsQuery.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListCustomModelsQuery(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission("admin.custom-models.view")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomModelDetailDto>> Get(
        Guid id,
        [FromQuery] int overwrittenPage = 1,
        [FromQuery] int overwrittenPageSize = GetCustomModelQuery.DefaultOverwrittenPageSize,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetCustomModelQuery(id, overwrittenPage, overwrittenPageSize), cancellationToken));

    /// <summary>Whether deployment is configured, over which transport, and its limits.</summary>
    [HttpGet("deployment-status")]
    [RequirePermission("admin.custom-models.view")]
    public async Task<ActionResult<DeploymentStatusDto>> GetDeploymentStatus(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetDeploymentStatusQuery(), cancellationToken));

    /// <summary>Parses the source URL for the Add dialog. Makes no outbound call.</summary>
    [HttpPost("source-preview")]
    [RequirePermission("admin.custom-models.manage")]
    public async Task<ActionResult<SourcePreviewDto>> PreviewSource(PreviewCustomModelSourceRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PreviewCustomModelSourceQuery(request.Source), cancellationToken));

    /// <summary>Saves the record and queues the deployment; returns before any transfer starts (FR-012).</summary>
    [HttpPost]
    [RequirePermission("admin.custom-models.manage")]
    [ProducesResponseType<SubmittedCustomModelDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmittedCustomModelDto>> Submit(SubmitCustomModelRequest request, CancellationToken cancellationToken)
    {
        var submitted = await mediator.Send(new SubmitCustomModelDeploymentCommand(request.Source, request.Destination, request.Name), cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = submitted.Id }, submitted);
    }

    /// <summary>A queued deployment is cancelled at once; a running one reports <c>Cancelled</c> over the hub once its job has stopped.</summary>
    [HttpPost("{id:guid}/actions/cancel")]
    [RequirePermission("admin.custom-models.manage")]
    [ProducesResponseType<CustomModelSummaryDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomModelSummaryDto>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Accepted(await mediator.Send(new CancelCustomModelDeploymentCommand(id), cancellationToken));

    /// <summary>Only a completed model can be made available, and only one per repository (FR-030).</summary>
    [HttpPut("{id:guid}/availability")]
    [RequirePermission("admin.custom-models.manage")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomModelSummaryDto>> SetAvailability(Guid id, SetCustomModelAvailabilityRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetCustomModelAvailabilityCommand(id, request.Availability), cancellationToken));

    /// <summary>Soft-deletes a failed or cancelled model. Files already on the deployment target are left in place (FR-031).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("admin.custom-models.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveCustomModelCommand(id), cancellationToken);
        return NoContent();
    }
}
