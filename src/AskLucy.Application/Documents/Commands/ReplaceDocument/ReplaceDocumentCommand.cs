using AskLucy.Application.Documents;
using AskLucy.Application.Documents.Commands.CompleteUploadAsVersion;
using MediatR;

namespace AskLucy.Application.Documents.Commands.ReplaceDocument;

/// <summary>contracts/document-versions-folders-api.md `POST /api/v1/documents/{documentId}/versions` — same chunked-upload session flow (`uploads` → chunks) as a new document, finalized here as a new version of an existing one instead (FR-038, FR-039).</summary>
public sealed record ReplaceDocumentCommand(Guid DocumentId, Guid UploadSessionId, VersionIncrement Increment) : IRequest<DocumentSummaryDto>;
