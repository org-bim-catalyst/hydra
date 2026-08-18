using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Memory.Commands.ApproveMemory;
using AskLucy.Application.Memory.Commands.ClearAllMemories;
using AskLucy.Application.Memory.Commands.DeleteMemory;
using AskLucy.Application.Memory.Commands.EditMemory;
using AskLucy.Application.Memory.Commands.MarkNotificationRead;
using AskLucy.Application.Memory.Commands.RejectMemory;
using AskLucy.Application.Memory.Commands.RequestMemoryExport;
using AskLucy.Application.Memory.Commands.ResolveMemoryConflict;
using AskLucy.Application.Memory.Commands.UpdateMemoryPreferences;
using AskLucy.Application.Memory.Queries.GetMemory;
using AskLucy.Application.Memory.Queries.GetMemoryExportStatus;
using AskLucy.Application.Memory.Queries.GetMemoryPreferences;
using AskLucy.Application.Memory.Queries.ListMemories;
using AskLucy.Application.Memory.Queries.ListMemoryNotifications;
using AskLucy.Domain.Memory;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// The Memory Center (contracts/memories-api.md, contracts/memory-privacy-api.md, spec.md
/// FR-017–FR-025, User Stories 2/3/4). Every response is implicitly scoped to the caller's own
/// memories — a request naming a memory the caller doesn't own returns 404, never 403 (FR-027,
/// least-information-disclosure).
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("memory-endpoints")]
[Route("api/v1/memories")]
public sealed class MemoriesController(ISender mediator, ISignedUrlService signedUrlService, IFileStorage fileStorage) : ControllerBase
{
    private static readonly TimeSpan ExportDownloadUrlLifetime = TimeSpan.FromMinutes(15);

    [HttpGet]
    public async Task<ActionResult<MemoryListResult>> List(
        [FromQuery] MemoryCategory? category = null,
        [FromQuery] MemoryLifecycleState? state = null,
        [FromQuery] string? projectId = null,
        [FromQuery] string? query = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var generalOnly = string.Equals(projectId, "general", StringComparison.OrdinalIgnoreCase);
        var parsedProjectId = !generalOnly && Guid.TryParse(projectId, out var parsed) ? parsed : (Guid?)null;

        return Ok(await mediator.Send(
            new ListMemoriesQuery(category, state, parsedProjectId, generalOnly, query, cursor, pageSize), cancellationToken));
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<MemoryPreferencesDto>> GetPreferences(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMemoryPreferencesQuery(), cancellationToken));

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(UpdateMemoryPreferencesRequest request, CancellationToken cancellationToken)
    {
        var categories = (request.Categories ?? [])
            .Select(c => new MemoryCategoryPreferenceUpdate(c.Category, c.ApprovalMode, c.IsEnabled))
            .ToList();

        await mediator.Send(new UpdateMemoryPreferencesCommand(request.MemoryEnabled, categories), cancellationToken);
        return NoContent();
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<PagedResult<MemoryNotificationDto>>> ListNotifications(
        [FromQuery] string? cursor = null, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListMemoryNotificationsQuery(cursor, pageSize), cancellationToken));

    [HttpPost("notifications/{id:guid}/actions/mark-read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MemoryDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMemoryQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, EditMemoryRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new EditMemoryCommand(id, request.Content), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteMemoryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ApproveMemoryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RejectMemoryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>spec.md FR-016, User Story 6 AC2/AC3 — `409` unless this memory has an open conflict awaiting confirmation (contracts/memories-api.md).</summary>
    [HttpPost("{id:guid}/actions/resolve-conflict")]
    public async Task<IActionResult> ResolveConflict(Guid id, ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResolveMemoryConflictCommand(id, request.Resolution), cancellationToken);
        return NoContent();
    }

    /// <summary>spec.md FR-023, User Story 4 AC2. Irreversible — requires explicit confirmation (contracts/memory-privacy-api.md).</summary>
    [HttpPost("actions/clear-all")]
    public async Task<IActionResult> ClearAll(ConfirmActionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ClearAllMemoriesCommand(request.Confirm), cancellationToken);
        return Accepted();
    }

    /// <summary>spec.md FR-024, User Story 4 AC3 — kicks off background generation; poll <see cref="GetExportStatus"/> for the signed download link.</summary>
    [HttpPost("actions/export")]
    public async Task<ActionResult<MemoryExportJobResponse>> RequestExport(CancellationToken cancellationToken)
    {
        var exportJobId = await mediator.Send(new RequestMemoryExportCommand(), cancellationToken);
        return Accepted(new MemoryExportJobResponse(exportJobId));
    }

    [HttpGet("exports/{exportJobId:guid}")]
    public async Task<ActionResult<MemoryExportStatusResponse>> GetExportStatus(Guid exportJobId, CancellationToken cancellationToken)
    {
        var status = await mediator.Send(new GetMemoryExportStatusQuery(exportJobId), cancellationToken);

        string? downloadUrl = null;
        if (status.StoredFileName is not null)
        {
            var (expires, signature) = signedUrlService.Sign(status.StoredFileName, ExportDownloadUrlLifetime);
            downloadUrl = Url.Action(nameof(DownloadExportContent), new { fileName = status.StoredFileName, exp = expires, sig = signature });
        }

        return Ok(new MemoryExportStatusResponse(status.Status, downloadUrl));
    }

    /// <summary>The actual byte stream — validates the signature itself rather than going through <c>[Authorize]</c>, exactly like <c>DocumentsController.DownloadContent</c> (a signed link is its own authorization).</summary>
    [HttpGet("exports/content")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadExportContent(
        [FromQuery] string fileName, [FromQuery] string exp, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        if (!signedUrlService.IsValid(fileName, exp, sig))
        {
            return Forbid();
        }

        var stream = await fileStorage.OpenReadAsync(fileName, cancellationToken);
        return File(stream, "application/json", "memory-export.json");
    }
}
