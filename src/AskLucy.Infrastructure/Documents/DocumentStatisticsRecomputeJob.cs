using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Documents;

internal static partial class DocumentStatisticsRecomputeJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Statistics recompute failed for owner {OwnerId} — will retry next cycle")]
    public static partial void OwnerRecomputeFailed(ILogger logger, Exception exception, string ownerId);
}

/// <summary>
/// Hangfire recurring job (research.md, data-model.md — "recomputed on a short interval, not
/// synchronously on every write") that refreshes every <see cref="DocumentStatistics"/> row: one
/// per distinct document owner (<see cref="DocumentStatisticsScope.User"/>) plus one
/// organization-wide row (<see cref="DocumentStatisticsScope.Organization"/>, US6 AC6). The
/// per-user dashboard's live counts (queue depth, in-progress, etc.) are computed directly from
/// <c>DocumentProcessingJob</c> on every request instead — only the slower-changing aggregates
/// here (totals, storage, distributions) are cached, since SC-011's 5-second accuracy budget
/// doesn't require these specific fields to be perfectly real-time.
/// </summary>
public sealed class DocumentStatisticsRecomputeJob(
    IDocumentStatisticsRepository statisticsRepository,
    IUnitOfWork unitOfWork,
    ILogger<DocumentStatisticsRecomputeJob> logger)
{
    private const string SystemActor = "system:statistics-recompute";

    public async Task RecomputeAllAsync(CancellationToken cancellationToken = default)
    {
        await RecomputeOrganizationAsync(cancellationToken);

        var ownerIds = await statisticsRepository.ListDistinctOwnerIdsAsync(cancellationToken);
        foreach (var ownerId in ownerIds)
        {
            try
            {
                await RecomputeForOwnerAsync(ownerId, cancellationToken);
            }
            catch (Exception ex)
            {
                // One owner's recompute failing (e.g. a transient query timeout) must not block
                // the rest of the sweep — every other owner's dashboard still deserves a fresh
                // snapshot this cycle.
                DocumentStatisticsRecomputeJobLog.OwnerRecomputeFailed(logger, ex, ownerId);
            }
        }
    }

    private async Task RecomputeOrganizationAsync(CancellationToken cancellationToken)
    {
        var aggregate = await statisticsRepository.ComputeAggregateAsync(ownerId: null, cancellationToken);
        var statistics = await statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.Organization, null, cancellationToken);

        if (statistics is null)
        {
            statistics = DocumentStatistics.CreateForOrganization(SystemActor);
            statisticsRepository.Add(statistics);
        }

        statistics.Refresh(
            aggregate.TotalDocuments, aggregate.TotalStorageBytes, aggregate.AverageProcessingDurationMs,
            aggregate.FileTypeDistributionJson, aggregate.LanguageDistributionJson, SystemActor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RecomputeForOwnerAsync(string ownerId, CancellationToken cancellationToken)
    {
        var aggregate = await statisticsRepository.ComputeAggregateAsync(ownerId, cancellationToken);
        var statistics = await statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.User, ownerId, cancellationToken);

        if (statistics is null)
        {
            statistics = DocumentStatistics.CreateForUser(ownerId, SystemActor);
            statisticsRepository.Add(statistics);
        }

        statistics.Refresh(
            aggregate.TotalDocuments, aggregate.TotalStorageBytes, aggregate.AverageProcessingDurationMs,
            aggregate.FileTypeDistributionJson, aggregate.LanguageDistributionJson, SystemActor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
