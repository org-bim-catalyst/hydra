using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Documents.Commands.UploadChunk;

/// <summary>Appends one chunk to the session's staged content (FR-005). Chunks must arrive in strict sequential order, matching the client contract (contracts/documents-api.md).</summary>
public sealed class UploadChunkCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IResumableUploadStorage resumableStorage,
    ICurrentUserAccessor currentUser) : IRequestHandler<UploadChunkCommand, UploadChunkResultDto>
{
    public async Task<UploadChunkResultDto> Handle(UploadChunkCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);
        session.EnsureInProgress();

        var sessionKey = session.Id.ToString();
        var currentSize = await resumableStorage.GetSizeAsync(sessionKey, cancellationToken);
        var expectedChunkIndex = (int)(currentSize / session.ChunkSizeBytes);

        if (request.ChunkIndex != expectedChunkIndex)
        {
            throw new DomainRuleViolationException(
                $"Expected chunk {expectedChunkIndex}, but received chunk {request.ChunkIndex}. Chunks must be uploaded in order.");
        }

        // constitution §8 (least privilege / secure defaults) — without this, an authenticated
        // caller could keep appending well-formed, in-order chunks forever without ever calling
        // Complete, consuming unbounded temp storage; CompleteUpload's declared-size check only
        // catches the mismatch after the fact. The session's own DeclaredSizeBytes was already
        // validated against MaxFileSizeBytes at StartUpload time, so this rejects any chunk that
        // would push accumulated storage past what this specific session is allowed to hold.
        var maxValidChunkIndex = (int)Math.Ceiling((double)session.DeclaredSizeBytes / session.ChunkSizeBytes) - 1;
        if (request.ChunkIndex > maxValidChunkIndex)
        {
            throw new DomainRuleViolationException("This chunk would exceed the upload's declared size.");
        }

        await resumableStorage.AppendChunkAsync(sessionKey, request.ChunkContent, cancellationToken);

        return new UploadChunkResultDto(request.ChunkIndex, expectedChunkIndex + 1);
    }
}
