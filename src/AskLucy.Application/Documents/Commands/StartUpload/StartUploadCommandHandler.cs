using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Documents.Commands.StartUpload;

/// <summary>Begins a resumable chunked upload session (FR-005, research.md Decision 6).</summary>
public sealed class StartUploadCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IDocumentRepository documentRepository,
    IDocumentStatisticsRepository statisticsRepository,
    IProcessingNotifier processingNotifier,
    IOptions<DocumentUploadOptions> uploadOptions,
    IOptions<DocumentStorageQuotaOptions> quotaOptions,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<StartUploadCommand, StartUploadResultDto>
{
    public async Task<StartUploadResultDto> Handle(StartUploadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (request.DeclaredSizeBytes > uploadOptions.Value.MaxFileSizeBytes)
        {
            throw new DomainRuleViolationException(
                $"File exceeds the maximum allowed size of {uploadOptions.Value.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        // FR-011, US6 AC4 — rejected before any chunk is transferred, not just at completion.
        var currentUsage = await statisticsRepository.ComputeAggregateAsync(userId, cancellationToken);
        if (currentUsage.TotalStorageBytes + request.DeclaredSizeBytes > quotaOptions.Value.DefaultQuotaBytes)
        {
            await processingNotifier.NotifyAsync(
                userId, DocumentNotificationEventType.StorageLimitReached, null,
                "Your storage limit has been reached — delete or archive documents to free up space before uploading more.",
                cancellationToken);

            throw new DomainRuleViolationException(
                $"This upload would exceed your storage limit of {quotaOptions.Value.DefaultQuotaBytes / (1024 * 1024 * 1024)} GB.");
        }

        if (request.TargetDocumentId is { } targetDocumentId)
        {
            DocumentOwnershipGuard.EnsureOwnedBy(
                await documentRepository.GetByIdAsync(targetDocumentId, cancellationToken), userId);
        }

        var expiresAtUtc = DateTime.UtcNow.Add(uploadOptions.Value.UploadSessionExpiry);
        var session = DocumentUploadSession.Create(
            userId, request.FileName, request.DeclaredSizeBytes, uploadOptions.Value.ChunkSizeBytes, expiresAtUtc, userId, request.TargetDocumentId);

        sessionRepository.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new StartUploadResultDto(session.Id, session.ChunkSizeBytes, session.ExpiresAtUtc);
    }
}
