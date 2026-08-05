using MediatR;

namespace AskLucy.Application.Documents.Commands.StartUpload;

/// <summary><paramref name="TargetDocumentId"/> is only set for a US5 replace-version upload (contracts/document-versions-folders-api.md's "?documentId={id}") — null for a plain new-document upload.</summary>
public sealed record StartUploadCommand(string FileName, long DeclaredSizeBytes, Guid? TargetDocumentId = null) : IRequest<StartUploadResultDto>;

public sealed record StartUploadResultDto(Guid UploadSessionId, long ChunkSizeBytes, DateTime ExpiresAtUtc);
