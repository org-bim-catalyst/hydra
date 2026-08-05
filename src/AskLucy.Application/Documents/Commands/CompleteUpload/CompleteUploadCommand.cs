using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CompleteUpload;

public sealed record CompleteUploadCommand(Guid UploadSessionId) : IRequest<CompleteUploadResultDto>;

/// <summary><see cref="Document"/> is set only when no duplicate was found; otherwise <see cref="DuplicateOfDocumentId"/> is set and the caller must call <c>CompleteUploadAsVersion</c>/<c>CompleteUploadAsNew</c> (FR-009).</summary>
public sealed record CompleteUploadResultDto(bool IsDuplicate, Guid? DuplicateOfDocumentId, DocumentSummaryDto? Document);
