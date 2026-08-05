using AskLucy.Domain.Documents;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Documents;

/// <summary>
/// Proves <see cref="DocumentRepository.SaveMetadataResolvingStalenessAsync"/>'s merge-and-warn
/// concurrency handling against a real SQL Server instance (FR-031a, research.md Decision 9) —
/// the actual EF Core <c>rowversion</c> conflict detection and reload/retry mechanics aren't
/// meaningfully fakeable, so <c>UpdateDocumentMetadataTests</c> (Application.Tests) covers the
/// handler's wiring while this proves the real database round-trip.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class DocumentMetadataConcurrencyTests(PersistenceTestFixture fixture)
{
    private async Task<(Guid DocumentId, byte[] StaleRowVersion)> SeedDocumentWithMetadataAsync(string ownerId)
    {
        var versionId = Guid.CreateVersion7();
        var checksum = DocumentChecksum.Create(new string('a', 64), ownerId);
        var document = Document.Create(Guid.CreateVersion7(), ownerId, "report.pdf", DocumentFileType.Pdf, 1024, versionId, ownerId);
        var version = DocumentVersion.Create(document.Id, 1, 0, "stored.bin", "report.pdf", 1024, checksum.Id, ownerId);
        var metadata = DocumentMetadata.CreateFromExtraction(document.Id, "Original Title", "Original Author", null, null, null, null, "system:processing");

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
        dbContext.Documents.Add(document);
        dbContext.DocumentChecksums.Add(checksum);
        dbContext.DocumentVersions.Add(version);
        dbContext.DocumentMetadata.Add(metadata);
        await dbContext.SaveChangesAsync();

        return (document.Id, metadata.RowVersion);
    }

    [Fact]
    public async Task SaveMetadataResolvingStalenessAsync_ShouldSucceedWithoutRetry_WhenNoConcurrentEditOccurred()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var (documentId, rowVersion) = await SeedDocumentWithMetadataAsync(ownerId);

        await using var dbContext = fixture.CreateDbContext();
        var repository = new DocumentRepository(dbContext);
        var metadata = await repository.GetMetadataByDocumentIdAsync(documentId, CancellationToken.None);
        metadata!.ApplyUserEdit("New Title", "New Author", null, null, "kw", ownerId);

        var wasStale = await repository.SaveMetadataResolvingStalenessAsync(
            metadata, rowVersion, m => m.ApplyUserEdit("New Title", "New Author", null, null, "kw", ownerId), CancellationToken.None);

        wasStale.Should().BeFalse();

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await new DocumentRepository(verifyContext).GetMetadataByDocumentIdAsync(documentId, CancellationToken.None);
        persisted!.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task SaveMetadataResolvingStalenessAsync_ShouldMergeAndReturnWasStaleTrue_WhenAnotherEditCommittedFirst()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var (documentId, staleRowVersion) = await SeedDocumentWithMetadataAsync(ownerId);

        // Simulate a second caller (a different browser tab) committing an edit first, using a
        // separate DbContext so its own SaveChanges genuinely advances the row's RowVersion in
        // the database before the "stale" caller below attempts to save.
        await using (var firstEditorContext = fixture.CreateDbContext())
        {
            var firstEditorRepository = new DocumentRepository(firstEditorContext);
            var metadata = await firstEditorRepository.GetMetadataByDocumentIdAsync(documentId, CancellationToken.None);
            metadata!.ApplyUserEdit("First Editor's Title", "First Editor", null, null, null, ownerId);
            await firstEditorContext.SaveChangesAsync();
        }

        // The "stale" caller loaded its own copy before the edit above committed, so its
        // RowVersion no longer matches what's in the database.
        await using var staleContext = fixture.CreateDbContext();
        var staleRepository = new DocumentRepository(staleContext);
        var staleMetadata = await staleRepository.GetMetadataByDocumentIdAsync(documentId, CancellationToken.None);
        staleMetadata!.ApplyUserEdit("Second Editor's Title", "Second Editor", null, null, "kw2", ownerId);

        var wasStale = await staleRepository.SaveMetadataResolvingStalenessAsync(
            staleMetadata, staleRowVersion,
            m => m.ApplyUserEdit("Second Editor's Title", "Second Editor", null, null, "kw2", ownerId),
            CancellationToken.None);

        wasStale.Should().BeTrue("the save must resolve via merge-and-warn, never a hard reject (FR-031a)");

        // Last-write-wins: the second (stale) caller's values are what ends up persisted, since
        // that's the edit that was re-applied on top of the reloaded latest state and saved last.
        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await new DocumentRepository(verifyContext).GetMetadataByDocumentIdAsync(documentId, CancellationToken.None);
        persisted!.Title.Should().Be("Second Editor's Title");
        persisted.Keywords.Should().Be("kw2");
    }
}
