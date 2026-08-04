using AskLucy.Application.Common;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.ActivateKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.ArchiveKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.CreateFolder;
using AskLucy.Application.KnowledgeBases.Commands.CreateKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.DeleteDocument;
using AskLucy.Application.KnowledgeBases.Commands.DeleteFolder;
using AskLucy.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.DuplicateKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.FavoriteKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.MoveDocument;
using AskLucy.Application.KnowledgeBases.Commands.MoveFolder;
using AskLucy.Application.KnowledgeBases.Commands.PinKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.PurgeKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.RenameFolder;
using AskLucy.Application.KnowledgeBases.Commands.RestoreKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.UnfavoriteKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.UnpinKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;
using AskLucy.Application.KnowledgeBases.Commands.UploadDocument;
using AskLucy.Application.KnowledgeBases.Queries.ExportKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseDashboardSummary;
using AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseFolderTree;
using AskLucy.Application.KnowledgeBases.Queries.ListKnowledgeBaseDocuments;
using AskLucy.Application.KnowledgeBases.Queries.SearchKnowledgeBases;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Every operation is implicitly scoped to the caller (FR-009/FR-010, contracts/knowledge-bases-api.md).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("knowledge-base-endpoints")]
[Route("api/v1/knowledge-bases")]
public sealed class KnowledgeBasesController(ISender mediator) : ControllerBase
{
    /// <summary>Search/filter/sort/paginate the caller's own knowledge bases (FR-022–FR-024, contracts/knowledge-bases-api.md).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<KnowledgeBaseSummaryDto>>> Search(
        [FromQuery] KnowledgeBaseListView view = KnowledgeBaseListView.Active,
        [FromQuery] string? q = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool? favorite = null,
        [FromQuery] bool? pinned = null,
        [FromQuery] KnowledgeBaseSort sort = KnowledgeBaseSort.RecentlyUpdated,
        [FromQuery] bool sortDescending = true,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(
            new SearchKnowledgeBasesQuery(view, q, categoryId, tag, favorite, pinned, sort, sortDescending, cursor, pageSize),
            cancellationToken));

    /// <summary>Cached per-user (research.md Decision 7, FR-035, contracts/knowledge-bases-api.md).</summary>
    [HttpGet("dashboard-summary")]
    public async Task<ActionResult<KnowledgeBaseDashboardSummaryDto>> GetDashboardSummary(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetKnowledgeBaseDashboardSummaryQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Create(CreateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        var knowledgeBase = await mediator.Send(
            new CreateKnowledgeBaseCommand(request.Name, request.Description, request.Color, request.Icon, request.CategoryId, request.Tags),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = knowledgeBase.Id }, knowledgeBase);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KnowledgeBaseDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetKnowledgeBaseQuery(id), cancellationToken));

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Update(Guid id, UpdateKnowledgeBaseDetailsRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdateKnowledgeBaseDetailsCommand(id, request.Name, request.Description, request.Color, request.Icon, request.CategoryId, request.Tags, request.Notes),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteKnowledgeBaseCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/activate")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Activate(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ActivateKnowledgeBaseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ArchiveKnowledgeBaseCommand(id), cancellationToken));

    /// <summary>Restores from soft-deleted (cancels the pending automatic purge, FR-036) or from Archived back to Active, whichever applies.</summary>
    [HttpPost("{id:guid}/actions/restore")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreKnowledgeBaseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/favorite")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Favorite(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new FavoriteKnowledgeBaseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/unfavorite")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Unfavorite(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UnfavoriteKnowledgeBaseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/pin")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Pin(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PinKnowledgeBaseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/unpin")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Unpin(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UnpinKnowledgeBaseCommand(id), cancellationToken));

    /// <summary>Deep copy — new knowledge base, own id, `status: Draft` (FR-032/FR-037, contracts/knowledge-bases-api.md).</summary>
    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<KnowledgeBaseSummaryDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var duplicate = await mediator.Send(new DuplicateKnowledgeBaseCommand(id), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = duplicate.Id }, duplicate);
    }

    /// <summary>Downloads a structured JSON metadata export (FR-033) — mirrors <c>ChatsController.Export</c>'s <c>File(...)</c> response shape.</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var export = await mediator.Send(new ExportKnowledgeBaseQuery(id), cancellationToken);
        var fileName = $"{SanitizeFileName(export.Name)}.json";
        return File(JsonSerializer.SerializeToUtf8Bytes(export), "application/json", fileName);
    }

    private static string SanitizeFileName(string name)
    {
        var sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(sanitized) ? "knowledge-base" : sanitized;
    }

    /// <summary>Permanent delete (FR-036) — irreversible; requires explicit confirmation (contracts/knowledge-bases-api.md).</summary>
    [HttpDelete("{id:guid}/actions/purge")]
    public async Task<IActionResult> Purge(Guid id, ConfirmActionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new PurgeKnowledgeBaseCommand(id, request.Confirm), cancellationToken);
        return NoContent();
    }

    // --- Folders (contracts/knowledge-base-folders-documents-api.md) ---

    [HttpGet("{knowledgeBaseId:guid}/folders")]
    public async Task<ActionResult<KnowledgeBaseFolderTreeDto>> GetFolderTree(Guid knowledgeBaseId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetKnowledgeBaseFolderTreeQuery(knowledgeBaseId), cancellationToken));

    [HttpPost("{knowledgeBaseId:guid}/folders")]
    public async Task<ActionResult<KnowledgeBaseFolderDto>> CreateFolder(Guid knowledgeBaseId, CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await mediator.Send(new CreateFolderCommand(knowledgeBaseId, request.Name, request.ParentFolderId), cancellationToken);
        return CreatedAtAction(nameof(GetFolderTree), new { knowledgeBaseId }, folder);
    }

    [HttpPatch("{knowledgeBaseId:guid}/folders/{folderId:guid}")]
    public async Task<ActionResult<KnowledgeBaseFolderDto>> RenameFolder(Guid knowledgeBaseId, Guid folderId, RenameFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RenameFolderCommand(knowledgeBaseId, folderId, request.Name), cancellationToken));

    [HttpPost("{knowledgeBaseId:guid}/folders/{folderId:guid}/actions/move")]
    public async Task<ActionResult<KnowledgeBaseFolderDto>> MoveFolder(Guid knowledgeBaseId, Guid folderId, MoveFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new MoveFolderCommand(knowledgeBaseId, folderId, request.NewParentFolderId), cancellationToken));

    [HttpDelete("{knowledgeBaseId:guid}/folders/{folderId:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid knowledgeBaseId, Guid folderId, [FromBody] DeleteFolderRequest? request, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteFolderCommand(knowledgeBaseId, folderId, request?.Confirm ?? false), cancellationToken);
        return NoContent();
    }

    // --- Documents (contracts/knowledge-base-folders-documents-api.md) ---

    [HttpGet("{knowledgeBaseId:guid}/documents")]
    public async Task<ActionResult<PagedResult<KnowledgeBaseDocumentDto>>> ListDocuments(
        Guid knowledgeBaseId, [FromQuery] Guid? folderId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListKnowledgeBaseDocumentsQuery(knowledgeBaseId, folderId), cancellationToken));

    [HttpPost("{knowledgeBaseId:guid}/documents")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<ActionResult<KnowledgeBaseDocumentDto>> UploadDocument(
        Guid knowledgeBaseId, IFormFile file, [FromForm] Guid? folderId, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var document = await mediator.Send(
            new UploadDocumentCommand(knowledgeBaseId, folderId, stream, file.FileName, file.Length), cancellationToken);
        return CreatedAtAction(nameof(ListDocuments), new { knowledgeBaseId }, document);
    }

    [HttpPost("{knowledgeBaseId:guid}/documents/{documentId:guid}/actions/move")]
    public async Task<ActionResult<KnowledgeBaseDocumentDto>> MoveDocument(
        Guid knowledgeBaseId, Guid documentId, MoveDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new MoveDocumentCommand(knowledgeBaseId, documentId, request.NewFolderId), cancellationToken));

    [HttpDelete("{knowledgeBaseId:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDocumentCommand(knowledgeBaseId, documentId), cancellationToken);
        return NoContent();
    }
}
