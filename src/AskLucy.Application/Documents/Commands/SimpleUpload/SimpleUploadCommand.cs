using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.SimpleUpload;

/// <summary>Single-request upload for small files (contracts/documents-api.md) — same validation/duplicate-detection path as the chunked flow (FR-009, FR-010), without the session/chunk round trips.</summary>
public sealed record SimpleUploadCommand(string FileName, Stream Content, long SizeBytes) : IRequest<SimpleUploadResultDto>;

/// <summary><see cref="UploadSessionId"/> is set only when <see cref="IsDuplicate"/> — the caller resolves it via <c>CompleteUploadAsVersion</c>/<c>CompleteUploadAsNew</c> using this id, same as the chunked flow.</summary>
public sealed record SimpleUploadResultDto(bool IsDuplicate, Guid? DuplicateOfDocumentId, Guid? UploadSessionId, DocumentSummaryDto? Document);
