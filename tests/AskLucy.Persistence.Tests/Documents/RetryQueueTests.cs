using AskLucy.Domain.Documents;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Documents;

/// <summary>
/// T118 (US6 AC2) — <c>DocumentProcessingJobRepository.GetRetryQueueAsync</c>/<c>GetDashboardCountsAsync</c>
/// against a real SQL Server instance. The "only the latest job per document" grouping (a
/// document that failed once and later succeeded after a retry must never double-count as both
/// Failed and Completed) is genuine LINQ-to-SQL (GroupBy + OrderByDescending + First per group) —
/// not meaningfully provable against a faked repository in Application.Tests.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class RetryQueueTests(PersistenceTestFixture fixture)
{
    private async Task<string> SeedOwnerAsync()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
        await dbContext.SaveChangesAsync();
        return ownerId;
    }

    private async Task<(Guid DocumentId, Guid VersionId)> SeedDocumentAsync(string ownerId, string fileName)
    {
        var checksum = DocumentChecksum.Create($"{Guid.NewGuid():N}{Guid.NewGuid():N}"[..64], ownerId);
        var documentId = Guid.CreateVersion7();
        var version = DocumentVersion.Create(documentId, 1, 0, "stored.bin", fileName, 1024, checksum.Id, ownerId);
        // DocumentVersion.Create generates its own Id internally (Guid.CreateVersion7()) — this
        // must be read back from the created entity, not a separately-generated local Guid, since
        // it's what DocumentProcessingJob.DocumentVersionId's real FK actually has to match.
        var document = Document.Create(documentId, ownerId, fileName, DocumentFileType.Pdf, 1024, version.Id, ownerId);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Documents.Add(document);
        dbContext.DocumentChecksums.Add(checksum);
        dbContext.DocumentVersions.Add(version);
        await dbContext.SaveChangesAsync();
        return (document.Id, version.Id);
    }

    [Fact]
    public async Task GetRetryQueueAsync_ShouldIncludeADocument_WhoseCurrentJobIsFailed()
    {
        var ownerId = await SeedOwnerAsync();
        var (documentId, versionId) = await SeedDocumentAsync(ownerId, "bad.pdf");

        var job = DocumentProcessingJob.Create(documentId, versionId, ownerId);
        job.Start("hangfire-1", ownerId);
        job.Fail("The file could not be parsed.", ownerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.DocumentProcessingJobs.Add(job);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new DocumentProcessingJobRepository(readContext);

        var retryQueue = await repository.GetRetryQueueAsync(ownerId, CancellationToken.None);

        retryQueue.Should().ContainSingle(e => e.DocumentId == documentId && e.FailureReason == "The file could not be parsed.");
    }

    [Fact]
    public async Task GetRetryQueueAsync_ShouldNotIncludeADocument_ThatFailedOnceButLaterSucceededAfterRetry()
    {
        var ownerId = await SeedOwnerAsync();
        var (documentId, versionId) = await SeedDocumentAsync(ownerId, "recovered.pdf");

        var job = DocumentProcessingJob.Create(documentId, versionId, ownerId);
        job.Start("hangfire-1", ownerId);
        job.Fail("Transient failure.", ownerId);
        job.Retry(ownerId);
        job.Start("hangfire-2", ownerId);
        job.Complete(ownerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.DocumentProcessingJobs.Add(job);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new DocumentProcessingJobRepository(readContext);

        var retryQueue = await repository.GetRetryQueueAsync(ownerId, CancellationToken.None);
        var counts = await repository.GetDashboardCountsAsync(ownerId, DateTime.UtcNow.Date, CancellationToken.None);

        retryQueue.Should().NotContain(e => e.DocumentId == documentId);
        counts.FailedCount.Should().Be(0);
        counts.CompletedTodayCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardCountsAsync_ShouldScopeToTheGivenOwner_NotAnotherOwnersDocuments()
    {
        var ownerId = await SeedOwnerAsync();
        var otherOwnerId = await SeedOwnerAsync();
        var (documentId, versionId) = await SeedDocumentAsync(ownerId, "mine.pdf");
        var (otherDocumentId, otherVersionId) = await SeedDocumentAsync(otherOwnerId, "theirs.pdf");

        var job = DocumentProcessingJob.Create(documentId, versionId, ownerId);
        var otherJob = DocumentProcessingJob.Create(otherDocumentId, otherVersionId, otherOwnerId);
        otherJob.Start("hangfire-1", otherOwnerId);
        otherJob.Fail("Not mine.", otherOwnerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.DocumentProcessingJobs.AddRange(job, otherJob);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new DocumentProcessingJobRepository(readContext);

        var counts = await repository.GetDashboardCountsAsync(ownerId, DateTime.UtcNow.Date, CancellationToken.None);

        counts.QueueDepth.Should().Be(1); // My own job, still Queued.
        counts.FailedCount.Should().Be(0); // The other owner's failed job must not leak in.
    }
}
