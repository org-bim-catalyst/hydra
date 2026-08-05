using AskLucy.Application.Documents.Commands.MarkNotificationRead;
using AskLucy.Application.Documents.Commands.RetryProcessing;
using AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;
using AskLucy.Application.Documents.Queries.GetDocumentProcessingStatus;
using AskLucy.Application.Documents.Queries.GetNotifications;
using AskLucy.Application.Documents.Queries.GetOrganizationDashboardSummary;
using AskLucy.Application.Documents.Queries.GetProcessingHistory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Per-document processing status/history/retry, dashboards, and notifications (FR-013, FR-027,
/// FR-029, FR-045, FR-045a, FR-047, contracts/document-processing-api.md). Every operation is
/// implicitly scoped to the caller (FR-048) except the organization dashboard, which is
/// deliberately admin-only (FR-045a) — mirrors <see cref="DocumentsController"/>.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("document-endpoints")]
[Route("api/v1/documents")]
public sealed class DocumentProcessingController(ISender mediator) : ControllerBase
{
    [HttpGet("{id:guid}/processing")]
    public async Task<ActionResult<DocumentProcessingStatusDto>> GetStatus(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetDocumentProcessingStatusQuery(id), cancellationToken));

    [HttpGet("{id:guid}/processing/history")]
    public async Task<ActionResult<IReadOnlyList<DocumentProcessingLogDto>>> GetHistory(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetProcessingHistoryQuery(id), cancellationToken));

    [HttpPost("{id:guid}/processing/actions/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RetryProcessingCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DocumentDashboardSummaryDto>> GetDashboard(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetDocumentDashboardSummaryQuery(), cancellationToken));

    [HttpGet("dashboard/organization")]
    [Authorize(Policy = "AdministratorOrSuperUser")]
    public async Task<ActionResult<DocumentDashboardSummaryDto>> GetOrganizationDashboard(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetOrganizationDashboardSummaryQuery(), cancellationToken));

    [HttpGet("notifications")]
    public async Task<ActionResult<DocumentNotificationPageDto>> GetNotifications(
        [FromQuery] bool unreadOnly, [FromQuery] string? cursor, [FromQuery] int pageSize, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationsQuery(unreadOnly, cursor, pageSize == 0 ? 50 : pageSize), cancellationToken));

    [HttpPost("notifications/{id:guid}/actions/mark-read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);
        return NoContent();
    }
}
