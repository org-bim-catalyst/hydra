using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentStatisticsRepository(AskLucyDbContext dbContext) : IDocumentStatisticsRepository
{
    public Task<DocumentStatistics?> GetByScopeAsync(DocumentStatisticsScope scope, string? ownerId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentStatistics.FirstOrDefaultAsync(s => s.Scope == scope && s.OwnerId == ownerId, cancellationToken);

    public void Add(DocumentStatistics statistics) => dbContext.DocumentStatistics.Add(statistics);

    public async Task<IReadOnlyList<string>> ListDistinctOwnerIdsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Documents.Select(d => d.OwnerId).Distinct().ToListAsync(cancellationToken);

    public async Task<DocumentStatisticsAggregate> ComputeAggregateAsync(string? ownerId, CancellationToken cancellationToken = default)
    {
        var documentsQuery = ownerId is null ? dbContext.Documents : dbContext.Documents.Where(d => d.OwnerId == ownerId);

        var totalDocuments = await documentsQuery.CountAsync(cancellationToken);

        var totalStorageBytes = await (
            from version in dbContext.DocumentVersions
            join document in documentsQuery on version.DocumentId equals document.Id
            select (long)version.SizeBytes)
            .SumAsync(cancellationToken);

        var completedDurationsMs = await (
            from job in dbContext.DocumentProcessingJobs
            join document in documentsQuery on job.DocumentId equals document.Id
            where job.Status == DocumentProcessingJobStatus.Completed && job.StartedAtUtc != null && job.CompletedAtUtc != null
            select EF.Functions.DateDiffMillisecond(job.StartedAtUtc!.Value, job.CompletedAtUtc!.Value))
            .ToListAsync(cancellationToken);
        long? averageProcessingDurationMs = completedDurationsMs.Count > 0
            ? (long)completedDurationsMs.Average()
            : null;

        var fileTypeDistribution = await documentsQuery
            .GroupBy(d => d.FileType)
            .Select(g => new { FileType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FileType.ToString(), x => x.Count, cancellationToken);

        var languageDistribution = await (
            from language in dbContext.DocumentLanguages
            join document in documentsQuery on language.DocumentId equals document.Id
            where language.Role == DocumentLanguageRole.Primary
            group language by language.LanguageCode into g
            select new { LanguageCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LanguageCode, x => x.Count, cancellationToken);

        return new DocumentStatisticsAggregate(
            totalDocuments,
            totalStorageBytes,
            averageProcessingDurationMs,
            JsonSerializer.Serialize(fileTypeDistribution),
            JsonSerializer.Serialize(languageDistribution));
    }
}
