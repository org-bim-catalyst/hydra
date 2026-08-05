using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents;
using AskLucy.Domain.Documents;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Documents;

/// <summary>
/// T093 — <c>DocumentRepository.SearchAsync</c>'s combined filter intersection (FR-035–FR-037)
/// against a real SQL Server instance. Each filter is expressed as a subquery against a separate
/// child table (DocumentMetadata/DocumentLanguage/DocumentClassification/Tags) with no EF Core
/// navigation properties tying them together (data-model.md) — a faked repository in
/// Application.Tests can't meaningfully prove the actual LINQ-to-SQL translation combines them
/// as an intersection rather than, say, silently ignoring one.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class DocumentSearchFilterTests(PersistenceTestFixture fixture)
{
    private readonly Dictionary<string, DocumentTag> _tagsByName = [];

    private async Task SeedOwnerAsync(string ownerId)
    {
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> SeedDocumentAsync(
        string ownerId, string fileName, string? author, string? languageCode, Guid? categoryId, DocumentProcessingStatus status, string? tagName)
    {
        var versionId = Guid.CreateVersion7();
        var checksum = DocumentChecksum.Create($"{Guid.NewGuid():N}{Guid.NewGuid():N}"[..64], ownerId);
        var document = Document.Create(Guid.CreateVersion7(), ownerId, fileName, DocumentFileType.Pdf, 1024, versionId, ownerId);
        var version = DocumentVersion.Create(document.Id, 1, 0, "stored.bin", fileName, 1024, checksum.Id, ownerId);
        document.SetProcessingStatus(status, ownerId);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Documents.Add(document);
        dbContext.DocumentChecksums.Add(checksum);
        dbContext.DocumentVersions.Add(version);

        if (author is not null)
        {
            dbContext.DocumentMetadata.Add(DocumentMetadata.CreateFromExtraction(document.Id, null, author, null, null, null, null, ownerId));
        }

        if (languageCode is not null)
        {
            dbContext.DocumentLanguages.Add(DocumentLanguage.Create(document.Id, languageCode, DocumentLanguageRole.Primary, 0.9m, ownerId));
        }

        if (categoryId is not null)
        {
            dbContext.DocumentClassifications.Add(DocumentClassification.CreateAutomatic(document.Id, categoryId.Value, 0.9m, ownerId));
        }

        if (tagName is not null)
        {
            // Tags are shared/reused per-owner (data-model.md) — reusing the same row across
            // documents here mirrors AddTagCommandHandler's find-before-create behavior, and
            // avoids violating the (OwnerId, Name) unique index by inserting the same tag twice.
            if (!_tagsByName.TryGetValue(tagName, out var tag))
            {
                tag = DocumentTag.Create(ownerId, tagName, ownerId);
                dbContext.DocumentTags.Add(tag);
                _tagsByName[tagName] = tag;
            }
            else
            {
                dbContext.Attach(tag);
            }

            document.AddTag(tag, ownerId);
        }

        await dbContext.SaveChangesAsync();
        return document.Id;
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnOnlyDocumentsMatchingEveryActiveFilter()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        await SeedOwnerAsync(ownerId);

        var category = DocumentCategory.Create($"Category-{Guid.NewGuid():N}", false, ownerId);
        var otherCategory = DocumentCategory.Create($"Other-{Guid.NewGuid():N}", false, ownerId);

        await using (var categoryContext = fixture.CreateDbContext())
        {
            categoryContext.DocumentCategories.AddRange(category, otherCategory);
            await categoryContext.SaveChangesAsync();
        }

        // Matches every filter below.
        var matchId = await SeedDocumentAsync(ownerId, "invoice-report.pdf", "Jane Doe", "en", category.Id, DocumentProcessingStatus.Completed, "Reviewed");

        // Each fails exactly one of the filters — proves the filters are ANDed, not ORed.
        await SeedDocumentAsync(ownerId, "invoice-report.pdf", "John Smith", "en", category.Id, DocumentProcessingStatus.Completed, "Reviewed"); // wrong author
        await SeedDocumentAsync(ownerId, "invoice-report.pdf", "Jane Doe", "ar", category.Id, DocumentProcessingStatus.Completed, "Reviewed"); // wrong language
        await SeedDocumentAsync(ownerId, "invoice-report.pdf", "Jane Doe", "en", otherCategory.Id, DocumentProcessingStatus.Completed, "Reviewed"); // wrong category
        await SeedDocumentAsync(ownerId, "invoice-report.pdf", "Jane Doe", "en", category.Id, DocumentProcessingStatus.Failed, "Reviewed"); // wrong status
        await SeedDocumentAsync(ownerId, "invoice-report.pdf", "Jane Doe", "en", category.Id, DocumentProcessingStatus.Completed, "Unrelated"); // wrong tag
        await SeedDocumentAsync(ownerId, "unrelated-file.pdf", "Jane Doe", "en", category.Id, DocumentProcessingStatus.Completed, "Reviewed"); // wrong filename

        await using var dbContext = fixture.CreateDbContext();
        var repository = new DocumentRepository(dbContext);
        var filters = new DocumentSearchFilters(
            Query: "invoice", Author: "Jane", LanguageCode: "en", Tag: "Reviewed",
            CategoryId: category.Id, Status: DocumentProcessingStatus.Completed);

        var (items, _) = await repository.SearchAsync(ownerId, DocumentListView.Active, null, filters, null, 50, CancellationToken.None);

        items.Should().ContainSingle(d => d.Id == matchId);
    }

    [Fact]
    public async Task SearchAsync_ShouldMatchTheQueryFilterAgainstMetadataTitleAndKeywords_NotJustFileName()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        await SeedOwnerAsync(ownerId);
        var documentId = await SeedDocumentAsync(ownerId, "unrelated-name.pdf", null, null, null, DocumentProcessingStatus.Completed, null);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.DocumentMetadata.Add(DocumentMetadata.CreateFromExtraction(documentId, "Quarterly Invoice Summary", null, null, null, null, null, ownerId));
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new DocumentRepository(readContext);
        var (items, _) = await repository.SearchAsync(
            ownerId, DocumentListView.Active, null, new DocumentSearchFilters(Query: "Invoice Summary"), null, 50, CancellationToken.None);

        items.Should().ContainSingle(d => d.Id == documentId);
    }

    [Fact]
    public async Task SearchAsync_ShouldFilterByDateRange()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        await SeedOwnerAsync(ownerId);
        var documentId = await SeedDocumentAsync(ownerId, "dated.pdf", null, null, null, DocumentProcessingStatus.Completed, null);

        await using var dbContext = fixture.CreateDbContext();
        var repository = new DocumentRepository(dbContext);

        var (withinRange, _) = await repository.SearchAsync(
            ownerId, DocumentListView.Active, null,
            new DocumentSearchFilters(DateFrom: DateTime.UtcNow.AddDays(-1), DateTo: DateTime.UtcNow.AddDays(1)),
            null, 50, CancellationToken.None);
        var (outsideRange, _) = await repository.SearchAsync(
            ownerId, DocumentListView.Active, null,
            new DocumentSearchFilters(DateFrom: DateTime.UtcNow.AddDays(1)),
            null, 50, CancellationToken.None);

        withinRange.Should().ContainSingle(d => d.Id == documentId);
        outsideRange.Should().BeEmpty();
    }
}
