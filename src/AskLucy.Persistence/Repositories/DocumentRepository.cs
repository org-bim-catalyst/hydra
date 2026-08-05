using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentRepository(AskLucyDbContext dbContext) : IDocumentRepository
{
    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Documents.Include(d => d.Tags).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Document?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Documents.IgnoreQueryFilters().Include(d => d.Tags).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(Document document) => dbContext.Documents.Add(document);

    public void AddVersion(DocumentVersion version) => dbContext.DocumentVersions.Add(version);

    public void AddChecksum(DocumentChecksum checksum) => dbContext.DocumentChecksums.Add(checksum);

    public Task<DocumentVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

    public async Task<IReadOnlyList<DocumentVersion>> GetVersionsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<string?> GetChecksumHashAsync(Guid checksumId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentChecksums.Where(c => c.Id == checksumId).Select(c => c.Hash).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<Document> Items, string? NextCursor)> SearchAsync(
        string ownerId, DocumentListView view, Guid? folderId, DocumentSearchFilters filters, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = view == DocumentListView.Deleted
            ? dbContext.Documents.IgnoreQueryFilters().Where(d => d.OwnerId == ownerId && d.DeletedAtUtc != null)
            : dbContext.Documents.Where(d => d.OwnerId == ownerId);

        query = view switch
        {
            DocumentListView.Active => query.Where(d => d.ArchivedAtUtc == null),
            DocumentListView.Archived => query.Where(d => d.ArchivedAtUtc != null),
            _ => query,
        };

        if (folderId is not null)
        {
            query = query.Where(d => d.FolderId == folderId);
        }

        // FR-035–FR-037: every filter below is optional and ANDed with the rest — no navigation
        // properties exist for these child rows (data-model.md), so each is expressed as an
        // Any() subquery against its own DbSet rather than a joined Include.
        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var q = filters.Query.Trim();
            query = query.Where(d =>
                d.FileName.Contains(q) ||
                dbContext.DocumentVersions.Any(v => v.DocumentId == d.Id && v.ExtractedText != null && v.ExtractedText.Contains(q)) ||
                dbContext.DocumentMetadata.Any(m => m.DocumentId == d.Id &&
                    ((m.Title != null && m.Title.Contains(q)) || (m.Keywords != null && m.Keywords.Contains(q)))));
        }

        if (!string.IsNullOrWhiteSpace(filters.Author))
        {
            var author = filters.Author.Trim();
            query = query.Where(d => dbContext.DocumentMetadata.Any(m => m.DocumentId == d.Id && m.Author != null && m.Author.Contains(author)));
        }

        if (!string.IsNullOrWhiteSpace(filters.LanguageCode))
        {
            query = query.Where(d => dbContext.DocumentLanguages.Any(l => l.DocumentId == d.Id && l.LanguageCode == filters.LanguageCode));
        }

        if (!string.IsNullOrWhiteSpace(filters.Tag))
        {
            query = query.Where(d => d.Tags.Any(t => t.Name == filters.Tag));
        }

        if (filters.CategoryId is { } categoryId)
        {
            query = query.Where(d => dbContext.DocumentClassifications.Any(c => c.DocumentId == d.Id && c.CategoryId == categoryId));
        }

        if (filters.DateFrom is { } dateFrom)
        {
            query = query.Where(d => d.CreatedAtUtc >= dateFrom);
        }

        if (filters.DateTo is { } dateTo)
        {
            query = query.Where(d => d.CreatedAtUtc <= dateTo);
        }

        if (filters.Status is { } status)
        {
            query = query.Where(d => d.ProcessingStatus == status);
        }

        var decodedCursor = DocumentCursor.Decode(cursor);
        if (decodedCursor is { } c)
        {
            query = query.Where(d =>
                d.CreatedAtUtc < c.CreatedAtUtc || (d.CreatedAtUtc == c.CreatedAtUtc && d.Id < c.Id));
        }

        var page = await query
            .Include(d => d.Tags)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenByDescending(d => d.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = page.Count > pageSize;
        var items = hasNextPage ? page.GetRange(0, pageSize) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = DocumentCursor.Encode(last.CreatedAtUtc, last.Id);
        }

        return (items, nextCursor);
    }

    public async Task<Guid?> FindDocumentIdByChecksumAsync(string ownerId, string hash, CancellationToken cancellationToken = default)
    {
        var match = await (
            from document in dbContext.Documents
            join version in dbContext.DocumentVersions on document.Id equals version.DocumentId
            join checksum in dbContext.DocumentChecksums on version.ChecksumId equals checksum.Id
            where document.OwnerId == ownerId && checksum.Hash == hash
            select (Guid?)document.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return match;
    }

    public void AddMetadata(DocumentMetadata metadata) => dbContext.DocumentMetadata.Add(metadata);

    public Task<DocumentMetadata?> GetMetadataByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentMetadata.FirstOrDefaultAsync(m => m.DocumentId == documentId, cancellationToken);

    public void AddLanguage(DocumentLanguage language) => dbContext.DocumentLanguages.Add(language);

    public async Task<IReadOnlyList<DocumentLanguage>> GetLanguagesByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentLanguages.Where(l => l.DocumentId == documentId).ToListAsync(cancellationToken);

    public void RemoveLanguages(IEnumerable<DocumentLanguage> languages) => dbContext.DocumentLanguages.RemoveRange(languages);

    public void AddClassification(DocumentClassification classification) => dbContext.DocumentClassifications.Add(classification);

    public Task<DocumentClassification?> GetClassificationByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentClassifications.FirstOrDefaultAsync(c => c.DocumentId == documentId, cancellationToken);

    public void AddPreview(DocumentPreview preview) => dbContext.DocumentPreviews.Add(preview);

    public async Task<IReadOnlyList<DocumentPreview>> GetPreviewsByVersionIdAsync(Guid documentVersionId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentPreviews.Where(p => p.DocumentVersionId == documentVersionId).ToListAsync(cancellationToken);

    public Task<DocumentPreview?> GetPreviewByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentPreviews.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DocumentCategory>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.DocumentCategories.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<DocumentCategory?> GetCategoryByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<bool> SaveMetadataResolvingStalenessAsync(
        DocumentMetadata metadata, byte[] clientRowVersion, Action<DocumentMetadata> reapplyEdit, CancellationToken cancellationToken = default)
    {
        dbContext.Entry(metadata).Property(m => m.RowVersion).OriginalValue = clientRowVersion;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            await dbContext.Entry(metadata).ReloadAsync(cancellationToken);
            reapplyEdit(metadata);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    public Task<DocumentTag?> FindTagByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default) =>
        dbContext.DocumentTags.FirstOrDefaultAsync(t => t.OwnerId == ownerId && t.Name == name, cancellationToken);

    public void AddTag(DocumentTag tag) => dbContext.DocumentTags.Add(tag);

    public async Task<IReadOnlyList<DocumentTag>> ListTagsByOwnerAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentTags.Where(t => t.OwnerId == ownerId).OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Document>> ListByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default) =>
        await dbContext.Documents.Include(d => d.Tags).Where(d => d.FolderId == folderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountDocumentsByFolderAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Documents
            .Where(d => d.OwnerId == ownerId && d.FolderId != null)
            .GroupBy(d => d.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.FolderId, x => x.Count);
    }

    public Task<int> CountDocumentsInFolderAsync(Guid folderId, CancellationToken cancellationToken = default) =>
        dbContext.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
}
