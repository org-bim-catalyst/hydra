using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Documents.Commands;

/// <summary>Either a brand-new <see cref="Document"/> was created, or a checksum match was found and the caller must resolve it (FR-009).</summary>
public sealed record DocumentUploadFinalizeResult(Document? Document, Guid? DuplicateOfDocumentId, string StoredFileName, string ChecksumHash)
{
    public bool IsDuplicate => DuplicateOfDocumentId is not null;
}

/// <summary>
/// Shared finalize logic for every upload path (<c>CompleteUploadCommand</c>,
/// <c>SimpleUploadCommand</c>) — validates content, enforces the size limit, computes the SHA-256
/// checksum (research.md Decision 8), checks for a duplicate (FR-009), and either creates a new
/// <see cref="Document"/>/<see cref="DocumentVersion"/> or reports the existing match. Not a
/// public interface — this is pure Application-layer orchestration reused by a handful of
/// command handlers in this same assembly, not a swappable Infrastructure concern.
/// </summary>
public sealed class DocumentUploadFinalizer(
    IDocumentFileValidator fileValidator,
    IFileStorage fileStorage,
    IDocumentRepository documentRepository,
    IDocumentStatisticsRepository statisticsRepository,
    IProcessingNotifier processingNotifier,
    IOptions<DocumentUploadOptions> uploadOptions,
    IOptions<DocumentStorageQuotaOptions> quotaOptions)
{
    public async Task<DocumentUploadFinalizeResult> FinalizeAsync(
        string ownerId, string fileName, Stream content, long declaredSizeBytes, string actor, CancellationToken cancellationToken)
    {
        if (declaredSizeBytes > uploadOptions.Value.MaxFileSizeBytes)
        {
            throw new DomainRuleViolationException(
                $"File exceeds the maximum allowed size of {uploadOptions.Value.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        await EnsureStorageQuotaAsync(ownerId, declaredSizeBytes, cancellationToken);

        var validation = await fileValidator.ValidateAsync(content, fileName, cancellationToken);
        if (!validation.IsValid)
        {
            throw new DomainRuleViolationException(validation.FailureReason ?? "The file content is not a supported document type.");
        }

        content.Position = 0;
        var hash = await ComputeSha256Async(content, cancellationToken);
        content.Position = 0;

        var existingDocumentId = await documentRepository.FindDocumentIdByChecksumAsync(ownerId, hash, cancellationToken);
        var storedFileName = await fileStorage.SaveAsync(content, fileName, cancellationToken);

        if (existingDocumentId is not null)
        {
            return new DocumentUploadFinalizeResult(null, existingDocumentId, storedFileName, hash);
        }

        var documentId = Guid.CreateVersion7();
        var checksum = DocumentChecksum.Create(hash, actor);
        var version = DocumentVersion.Create(documentId, 1, 0, storedFileName, fileName, declaredSizeBytes, checksum.Id, actor);
        var document = Document.Create(documentId, ownerId, fileName, validation.DetectedType!.Value, declaredSizeBytes, version.Id, actor);

        documentRepository.AddChecksum(checksum);
        documentRepository.AddVersion(version);
        documentRepository.Add(document);

        return new DocumentUploadFinalizeResult(document, null, storedFileName, hash);
    }

    /// <summary>Internal (not private) — reused by <c>ReplaceDocumentCommandHandler</c> (US5), which needs the same hash computation for a version-replace upload that deliberately bypasses this class's cross-document duplicate check.</summary>
    internal static async Task<string> ComputeSha256Async(Stream content, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(content, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// FR-011, US6 AC4 — rejected before any expensive validation/hashing/storage work, with a
    /// <see cref="DocumentNotificationEventType.StorageLimitReached"/> notification fired
    /// alongside the rejection (FR-047) so the user sees why, not just that the upload failed.
    /// <see cref="IDocumentStatisticsRepository.ComputeAggregateAsync"/> is reused here rather
    /// than a second bespoke "total storage" query — its <c>TotalStorageBytes</c> already sums
    /// every version's stored bytes for this owner, exactly what quota enforcement needs.
    /// </summary>
    private async Task EnsureStorageQuotaAsync(string ownerId, long additionalBytes, CancellationToken cancellationToken)
    {
        var currentUsage = await statisticsRepository.ComputeAggregateAsync(ownerId, cancellationToken);
        if (currentUsage.TotalStorageBytes + additionalBytes <= quotaOptions.Value.DefaultQuotaBytes)
        {
            return;
        }

        await processingNotifier.NotifyAsync(
            ownerId, DocumentNotificationEventType.StorageLimitReached, null,
            "Your storage limit has been reached — delete or archive documents to free up space before uploading more.",
            cancellationToken);

        throw new DomainRuleViolationException(
            $"This upload would exceed your storage limit of {quotaOptions.Value.DefaultQuotaBytes / (1024 * 1024 * 1024)} GB.");
    }
}
