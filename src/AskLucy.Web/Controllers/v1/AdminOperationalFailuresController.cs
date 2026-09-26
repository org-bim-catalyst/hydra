using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.OperationalFailures.Commands.AcknowledgeIncident;
using AskLucy.Application.OperationalFailures.Commands.AcknowledgeRootCause;
using AskLucy.Application.OperationalFailures.Commands.ReopenIncident;
using AskLucy.Application.OperationalFailures.Commands.ResolveIncident;
using AskLucy.Application.OperationalFailures.Commands.ResolveRootCause;
using AskLucy.Application.OperationalFailures.Investigations.GetChatInvestigation;
using AskLucy.Application.OperationalFailures.Queries.GetIncident;
using AskLucy.Application.OperationalFailures.Queries.GetSummary;
using AskLucy.Application.OperationalFailures.Queries.ListIncidents;
using AskLucy.Application.OperationalFailures.Queries.ListOccurrences;
using AskLucy.Application.OperationalFailures.Queries.ListRelatedIncidents;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/074 contracts/admin-operational-failures.md — the administrator's operational failure
/// trail. Investigations are read-only by construction: this controller has no write route under
/// them (FR-016b), and whether a transcript is included is decided by the handler, never the client.
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/operational-failures")]
public sealed class AdminOperationalFailuresController(ISender mediator) : ControllerBase
{
    [HttpGet("incidents")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<PagedResult<IncidentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<IncidentSummaryDto>>> ListIncidents(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] IncidentStateFilter? state,
        [FromQuery] OperationalFailureSeverity[]? severity,
        [FromQuery] OperationalFailureEngine[]? engine,
        [FromQuery] string? provider,
        [FromQuery] OperationalFailureKind[]? kind,
        [FromQuery] string? userId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25) =>
        Ok(await mediator.Send(
            new ListIncidentsQuery(ToUtc(from), ToUtc(to), state, severity, engine, provider, kind, userId, page, pageSize),
            cancellationToken));

    [HttpGet("incidents/{id:guid}")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentDetailDto>> GetIncident(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetIncidentQuery(id), cancellationToken));

    [HttpGet("incidents/{id:guid}/occurrences")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<PagedResult<OccurrenceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<OccurrenceDto>>> ListOccurrences(
        Guid id, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(await mediator.Send(new ListOccurrencesQuery(id, page, pageSize), cancellationToken));

    [HttpGet("incidents/{id:guid}/related")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<PagedResult<IncidentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<IncidentSummaryDto>>> ListRelatedIncidents(
        Guid id, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await mediator.Send(new ListRelatedIncidentsQuery(id, page, pageSize), cancellationToken));

    [HttpGet("summary")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<OperationalFailureSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OperationalFailureSummaryDto>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetOperationalFailureSummaryQuery(), cancellationToken));

    [HttpPost("incidents/{id:guid}/actions/acknowledge")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncidentDetailDto>> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new AcknowledgeIncidentCommand(id), cancellationToken);
        return Ok(await mediator.Send(new GetIncidentQuery(id), cancellationToken));
    }

    [HttpPost("incidents/{id:guid}/actions/resolve")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncidentDetailDto>> Resolve(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResolveRequest? request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResolveIncidentCommand(id, request?.Note), cancellationToken);
        return Ok(await mediator.Send(new GetIncidentQuery(id), cancellationToken));
    }

    [HttpPost("incidents/{id:guid}/actions/reopen")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncidentDetailDto>> Reopen(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ReopenIncidentCommand(id), cancellationToken);
        return Ok(await mediator.Send(new GetIncidentQuery(id), cancellationToken));
    }

    [HttpPost("root-causes/{rootCauseKey}/actions/acknowledge")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<BulkTransitionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BulkTransitionResultDto>> AcknowledgeRootCause(string rootCauseKey, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AcknowledgeRootCauseCommand(rootCauseKey), cancellationToken));

    [HttpPost("root-causes/{rootCauseKey}/actions/resolve")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresManage)]
    [ProducesResponseType<BulkTransitionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BulkTransitionResultDto>> ResolveRootCause(
        string rootCauseKey, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResolveRequest? request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ResolveRootCauseCommand(rootCauseKey, request?.Note), cancellationToken));

    /// <summary>Metadata for anyone with view; the transcript only for a holder of <c>content.view</c>, and every such read is audited.</summary>
    [HttpGet("incidents/{incidentId:guid}/chats/{chatId:guid}")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<ChatInvestigationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatInvestigationDto>> GetChatInvestigation(
        Guid incidentId, Guid chatId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetChatInvestigationQuery(incidentId, chatId), cancellationToken));

    /// <summary>The optional body of a resolve; the note is capped at 500 characters.</summary>
    public sealed record ResolveRequest(string? Note);

    /// <summary>A range without an offset is taken as UTC, which is what the contract specifies.</summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
    };
}
