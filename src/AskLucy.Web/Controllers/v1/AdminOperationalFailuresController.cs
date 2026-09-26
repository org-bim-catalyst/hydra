using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.OperationalFailures.Investigations.GetChatInvestigation;
using AskLucy.Application.OperationalFailures.Queries.GetIncident;
using AskLucy.Application.OperationalFailures.Queries.ListIncidents;
using AskLucy.Application.OperationalFailures.Queries.ListOccurrences;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Web.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;
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
        [FromQuery] OperationalFailureSeverity? severity,
        [FromQuery] OperationalFailureEngine? engine,
        [FromQuery] string? provider,
        [FromQuery] OperationalFailureKind? kind,
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

    /// <summary>Metadata for anyone with view; the transcript only for a holder of <c>content.view</c>, and every such read is audited.</summary>
    [HttpGet("incidents/{incidentId:guid}/chats/{chatId:guid}")]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<ChatInvestigationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatInvestigationDto>> GetChatInvestigation(
        Guid incidentId, Guid chatId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetChatInvestigationQuery(incidentId, chatId), cancellationToken));

    /// <summary>A range without an offset is taken as UTC, which is what the contract specifies.</summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
    };
}
