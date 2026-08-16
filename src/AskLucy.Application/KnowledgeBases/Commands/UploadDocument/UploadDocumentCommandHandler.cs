using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.KnowledgeBases.Commands.UploadDocument;

/// <summary>
/// Validates content by magic-byte signature (research.md Decision 8, constitution §8),
/// saves via <see cref="IFileStorage"/>, best-effort extracts a page count (research.md
/// Decision 5 — a parse failure marks <see cref="KnowledgeBaseDocumentProcessingStatus.Failed"/>
/// but never blocks the upload itself), and updates the owning knowledge base's cached
/// statistics (FR-030/FR-031).
/// </summary>
public sealed class UploadDocumentCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    IDocumentContentValidator contentValidator,
    IDocumentPageCountExtractor pageCountExtractor,
    IFileStorage fileStorage,
    IOptions<KnowledgeBaseDocumentOptions> documentOptions,
    KnowledgeBaseDashboardSummaryCache dashboardSummaryCache,
    IPublisher publisher,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UploadDocumentCommand, KnowledgeBaseDocumentDto>
{
    private static readonly HashSet<KnowledgeBaseDocumentType> PaginatedTypes =
        [KnowledgeBaseDocumentType.Pdf, KnowledgeBaseDocumentType.Word, KnowledgeBaseDocumentType.PowerPoint];

    public async Task<KnowledgeBaseDocumentDto> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);

        if (request.FolderId is { } folderId)
        {
            KnowledgeBaseFolderGuard.EnsureBelongsTo(await folderRepository.GetByIdAsync(folderId, cancellationToken), request.KnowledgeBaseId);
        }

        if (request.SizeBytes > documentOptions.Value.MaxFileSizeBytes)
        {
            throw new DomainRuleViolationException(
                $"File exceeds the maximum allowed size of {documentOptions.Value.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var validation = await contentValidator.ValidateAsync(request.Content, request.FileName, cancellationToken);
        if (!validation.IsValid)
        {
            throw new DomainRuleViolationException(validation.FailureReason ?? "The file content is not a supported document type.");
        }

        var storedFileName = await fileStorage.SaveAsync(request.Content, request.FileName, cancellationToken);

        request.Content.Position = 0;
        var pageCount = await pageCountExtractor.ExtractPageCountAsync(request.Content, validation.DetectedType!.Value, cancellationToken);
        var extractionFailed = pageCount is null && PaginatedTypes.Contains(validation.DetectedType!.Value);

        var document = KnowledgeBaseDocument.Create(
            request.KnowledgeBaseId, request.FolderId, request.FileName, storedFileName, validation.ResolvedContentType!, request.SizeBytes, pageCount, userId);
        if (extractionFailed)
        {
            document.MarkProcessingFailed(userId);
        }

        documentRepository.Add(document);
        knowledgeBase.ApplyDocumentAdded(pageCount, request.SizeBytes, userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        dashboardSummaryCache.Invalidate(userId);

        // research.md Decision 12 — the event-trigger dispatch point for FR-063's "document
        // uploaded" trigger; published only after the commit above has succeeded.
        await publisher.Publish(new DocumentUploadedNotification(document.Id, request.KnowledgeBaseId, userId, request.FileName), cancellationToken);

        return KnowledgeBaseDocumentDto.FromEntity(document);
    }
}
