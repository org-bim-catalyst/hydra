using AskLucy.Application.Documents;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using AskLucy.Application.Documents.Commands.ReplaceDocument;
using AskLucy.Application.Documents.Commands.RestoreDocumentVersion;
using AskLucy.Application.Documents.Queries.CompareVersions;
using AskLucy.Application.Documents.Queries.GetVersionTimeline;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// US5 versioning (FR-038–FR-042, contracts/document-versions-folders-api.md). Every operation
/// is implicitly scoped to the caller (FR-048), mirroring <see cref="DocumentsController"/>.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("document-endpoints")]
[Route("api/v1/documents/{documentId:guid}/versions")]
public sealed class DocumentVersionsController(ISender mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<DocumentSummaryDto>> Replace(Guid documentId, ReplaceDocumentRequest request, CancellationToken cancellationToken)
    {
        var increment = Enum.Parse<VersionIncrement>(request.VersionIncrement, ignoreCase: true);
        var result = await mediator.Send(new ReplaceDocumentCommand(documentId, request.UploadSessionId, increment), cancellationToken);
        return CreatedAtAction(nameof(GetTimeline), new { documentId }, result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentVersionSummaryDto>>> GetTimeline(Guid documentId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetVersionTimelineQuery(documentId), cancellationToken));

    [HttpGet("compare")]
    public async Task<ActionResult<DocumentVersionCompareDto>> Compare(
        Guid documentId, [FromQuery] Guid fromVersionId, [FromQuery] Guid toVersionId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CompareVersionsQuery(documentId, fromVersionId, toVersionId), cancellationToken));

    [HttpPost("{versionId:guid}/actions/restore")]
    public async Task<ActionResult<DocumentSummaryDto>> Restore(Guid documentId, Guid versionId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreDocumentVersionCommand(documentId, versionId), cancellationToken));
}
