using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Documents;
using AskLucy.Application.Documents.Commands.AddTag;
using AskLucy.Application.Documents.Commands.ArchiveDocument;
using AskLucy.Application.Documents.Commands.CancelUpload;
using AskLucy.Application.Documents.Commands.CompleteUpload;
using AskLucy.Application.Documents.Commands.CompleteUploadAsNew;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Commands.CreateFolder;
using AskLucy.Application.Documents.Commands.DeleteDocument;
using AskLucy.Application.Documents.Commands.DeleteFolder;
using AskLucy.Application.Documents.Commands.DuplicateDocument;
using AskLucy.Application.Documents.Commands.MoveDocument;
using AskLucy.Application.Documents.Commands.MoveFolder;
using AskLucy.Application.Documents.Commands.OverrideClassification;
using AskLucy.Application.Documents.Commands.RemoveTag;
using AskLucy.Application.Documents.Commands.RenameDocument;
using AskLucy.Application.Documents.Commands.RenameFolder;
using AskLucy.Application.Documents.Commands.RestoreDocument;
using AskLucy.Application.Documents.Commands.SimpleUpload;
using AskLucy.Application.Documents.Commands.StartUpload;
using AskLucy.Application.Documents.Commands.UpdateDocumentMetadata;
using AskLucy.Application.Documents.Commands.UploadChunk;
using AskLucy.Application.Documents.Queries.GetDocument;
using AskLucy.Application.Documents.Queries.GetDocumentDownloadUrl;
using AskLucy.Application.Documents.Queries.GetDocumentPreview;
using AskLucy.Application.Documents.Queries.GetFolderTree;
using AskLucy.Application.Documents.Queries.ListDocumentCategories;
using AskLucy.Application.Documents.Queries.ListTags;
using AskLucy.Application.Documents.Queries.SearchDocuments;
using AskLucy.Domain.Documents;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Every operation is implicitly scoped to the caller (FR-048, contracts/documents-api.md).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("document-endpoints")]
[Route("api/v1/documents")]
public sealed class DocumentsController(
    ISender mediator, ISignedUrlService signedUrlService, IDocumentRepository documents, IFileStorage fileStorage) : ControllerBase
{
    private static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(15);

    /// <summary><paramref name="documentId"/> (contracts/document-versions-folders-api.md's "?documentId={id}") marks this as a US5 replace-version upload rather than a plain new-document upload.</summary>
    [HttpPost("uploads")]
    [EnableRateLimiting("document-upload-chunk-endpoints")]
    public async Task<ActionResult<StartUploadResultDto>> StartUpload(StartUploadRequest request, [FromQuery] Guid? documentId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new StartUploadCommand(request.FileName, request.SizeBytes, documentId), cancellationToken));

    [HttpPut("uploads/{uploadSessionId:guid}/chunks/{chunkIndex:int}")]
    [EnableRateLimiting("document-upload-chunk-endpoints")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<UploadChunkResultDto>> UploadChunk(Guid uploadSessionId, int chunkIndex, CancellationToken cancellationToken)
    {
        await using var stream = Request.Body;
        return Ok(await mediator.Send(new UploadChunkCommand(uploadSessionId, chunkIndex, stream), cancellationToken));
    }

    [HttpPost("uploads/{uploadSessionId:guid}/complete")]
    public async Task<ActionResult<CompleteUploadResultDto>> CompleteUpload(Guid uploadSessionId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CompleteUploadCommand(uploadSessionId), cancellationToken);
        return result.IsDuplicate ? Conflict(result) : Ok(result);
    }

    [HttpPost("uploads/{uploadSessionId:guid}/complete-as-version")]
    public async Task<ActionResult<DocumentSummaryDto>> CompleteUploadAsVersion(
        Guid uploadSessionId, CompleteUploadAsVersionRequest request, CancellationToken cancellationToken)
    {
        var increment = Enum.Parse<VersionIncrement>(request.VersionIncrement, ignoreCase: true);
        return Ok(await mediator.Send(
            new CompleteUploadAsVersionCommand(uploadSessionId, request.ExistingDocumentId, increment), cancellationToken));
    }

    [HttpPost("uploads/{uploadSessionId:guid}/complete-as-new")]
    public async Task<ActionResult<DocumentSummaryDto>> CompleteUploadAsNew(Guid uploadSessionId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CompleteUploadAsNewCommand(uploadSessionId), cancellationToken));

    [HttpDelete("uploads/{uploadSessionId:guid}")]
    public async Task<IActionResult> CancelUpload(Guid uploadSessionId, CancellationToken cancellationToken)
    {
        await mediator.Send(new CancelUploadCommand(uploadSessionId), cancellationToken);
        return NoContent();
    }

    [HttpPost("uploads/simple")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<SimpleUploadResultDto>> SimpleUpload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await mediator.Send(new SimpleUploadCommand(file.FileName, stream, file.Length), cancellationToken);
        return result.IsDuplicate ? Conflict(result) : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<DocumentSummaryDto>>> Search(
        [FromQuery] DocumentListView view = DocumentListView.Active,
        [FromQuery] Guid? folderId = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] string? author = null,
        [FromQuery] string? language = null,
        [FromQuery] string? tag = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] DocumentProcessingStatus? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(
            new SearchDocumentsQuery(view, folderId, cursor, pageSize, q, author, language, tag, categoryId, dateFrom, dateTo, status),
            cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDetailDto>> GetDocument(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetDocumentQuery(id), cancellationToken));

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<DocumentSummaryDto>> Rename(Guid id, RenameDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RenameDocumentCommand(id, request.FileName), cancellationToken));

    [HttpPatch("{id:guid}/metadata")]
    public async Task<ActionResult<UpdateDocumentMetadataResult>> UpdateMetadata(Guid id, UpdateDocumentMetadataRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdateDocumentMetadataCommand(id, request.RowVersion, request.Title, request.Author, request.CreationDate, request.ModificationDate, request.Keywords),
            cancellationToken));

    [HttpPut("{id:guid}/classification")]
    public async Task<ActionResult<DocumentClassificationDto>> OverrideClassification(Guid id, OverrideClassificationRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new OverrideClassificationCommand(id, request.CategoryId), cancellationToken));

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<string>>> ListTags(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListTagsQuery(), cancellationToken));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<DocumentCategoryDto>>> ListCategories(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListDocumentCategoriesQuery(), cancellationToken));

    [HttpPost("{id:guid}/tags")]
    public async Task<ActionResult<IReadOnlyList<string>>> AddTag(Guid id, AddTagRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AddTagCommand(id, request.Name), cancellationToken));

    [HttpDelete("{id:guid}/tags/{tagName}")]
    public async Task<IActionResult> RemoveTag(Guid id, string tagName, CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveTagCommand(id, tagName), cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/folder")]
    public async Task<ActionResult<DocumentSummaryDto>> MoveDocument(Guid id, MoveDocumentToFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new MoveDocumentCommand(id, request.FolderId), cancellationToken));

    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<DocumentSummaryDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DuplicateDocumentCommand(id), cancellationToken);
        return CreatedAtAction(nameof(GetDocument), new { id = result.Id }, result);
    }

    [HttpPost("folders")]
    public async Task<ActionResult<DocumentFolderDto>> CreateFolder(CreateDocumentFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CreateFolderCommand(request.Name, request.ParentFolderId), cancellationToken));

    [HttpPatch("folders/{id:guid}")]
    public async Task<ActionResult<DocumentFolderDto>> RenameFolder(Guid id, RenameDocumentFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RenameFolderCommand(id, request.Name), cancellationToken));

    [HttpPatch("folders/{id:guid}/parent")]
    public async Task<ActionResult<DocumentFolderDto>> MoveFolder(Guid id, MoveDocumentFolderRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new MoveFolderCommand(id, request.ParentFolderId), cancellationToken));

    [HttpDelete("folders/{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id, [FromQuery] OnContainedDocumentsAction? onContainedDocuments, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteFolderCommand(id, onContainedDocuments), cancellationToken);
        return NoContent();
    }

    [HttpGet("folders/tree")]
    public async Task<ActionResult<IReadOnlyList<DocumentFolderDto>>> GetFolderTree(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetFolderTreeQuery(), cancellationToken));

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ArchiveDocumentCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreDocumentCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDocumentCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Issues a signed, time-limited download URL as JSON (FR-015, FR-018, FR-050) — never a
    /// physical path — mirrors <c>UsersController</c>'s avatar-upload response shape. A 302
    /// redirect was considered and rejected: this endpoint requires <c>[Authorize]</c> (a Bearer
    /// token), but a browser's plain navigation to a redirect target never attaches one, so the
    /// client must call this over an authenticated <c>fetch</c> first and then navigate to the
    /// returned (separately <c>[AllowAnonymous]</c>, signature-authorized) URL itself.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<ActionResult<DocumentDownloadUrlResponse>> Download(Guid id, [FromQuery] Guid? versionId, CancellationToken cancellationToken)
    {
        var token = await mediator.Send(new GetDocumentDownloadTokenQuery(id, versionId), cancellationToken);
        var (expires, signature) = signedUrlService.Sign(token.VersionId.ToString(), DownloadUrlLifetime);
        var url = Url.Action(nameof(DownloadContent), new { versionId = token.VersionId, exp = expires, sig = signature })!;

        return Ok(new DocumentDownloadUrlResponse(url, token.OriginalFileName));
    }

    /// <summary>The actual byte stream — validates the signature itself rather than going through <c>[Authorize]</c>, exactly like <c>UsersController.GetAvatar</c> (a signed link is its own authorization).</summary>
    [HttpGet("versions/{versionId:guid}/download-content")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadContent(Guid versionId, [FromQuery] string exp, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        if (!signedUrlService.IsValid(versionId.ToString(), exp, sig))
        {
            return Forbid();
        }

        var version = await documents.GetVersionByIdAsync(versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        return File(stream, "application/octet-stream", version.OriginalFileName);
    }

    /// <summary>FR-043, FR-044 — never an error state; <see cref="DocumentPreviewKind.Unavailable"/> means the client shows "no preview available" and offers download instead.</summary>
    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<DocumentPreviewResponse>> GetPreview(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDocumentPreviewQuery(id), cancellationToken);

        string? url = null;
        if (result.PreviewId is { } previewId)
        {
            var (expires, signature) = signedUrlService.Sign(previewId.ToString(), DownloadUrlLifetime);
            url = Url.Action(nameof(PreviewContent), new { previewId, exp = expires, sig = signature });
        }

        return Ok(new DocumentPreviewResponse(result.PreviewType, url, result.StructuredContent));
    }

    /// <summary>The actual rendered preview image bytes — same signed-link-is-its-own-authorization pattern as <see cref="DownloadContent"/>.</summary>
    [HttpGet("previews/{previewId:guid}/download-content")]
    [AllowAnonymous]
    public async Task<IActionResult> PreviewContent(Guid previewId, [FromQuery] string exp, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        if (!signedUrlService.IsValid(previewId.ToString(), exp, sig))
        {
            return Forbid();
        }

        var preview = await documents.GetPreviewByIdAsync(previewId, cancellationToken);
        if (preview?.StoredFileName is null)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(preview.StoredFileName, cancellationToken);
        return File(stream, "image/png");
    }
}
