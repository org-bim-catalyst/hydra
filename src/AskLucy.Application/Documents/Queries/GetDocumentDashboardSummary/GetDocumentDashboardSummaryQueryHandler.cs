using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;

/// <summary>
/// FR-045, US6 AC1 — per-user dashboard. <see cref="TimeProvider"/> (not <c>DateTime.UtcNow</c>
/// directly) so "completed today" is deterministically testable without waiting for a real UTC
/// day boundary (mirrors <c>KnowledgeBasePurgeHostedService</c>'s convention).
/// </summary>
public sealed class GetDocumentDashboardSummaryQueryHandler(
    IDocumentProcessingJobRepository jobRepository,
    IDocumentStatisticsRepository statisticsRepository,
    TimeProvider timeProvider,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetDocumentDashboardSummaryQuery, DocumentDashboardSummaryDto>
{
    public async Task<DocumentDashboardSummaryDto> Handle(GetDocumentDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var todayStartUtc = timeProvider.GetUtcNow().UtcDateTime.Date;

        var counts = await jobRepository.GetDashboardCountsAsync(userId, todayStartUtc, cancellationToken);
        var retryQueue = await jobRepository.GetRetryQueueAsync(userId, cancellationToken);
        var statistics = await statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.User, userId, cancellationToken);

        return new DocumentDashboardSummaryDto(
            counts.QueueDepth,
            counts.InProgressCount,
            counts.CompletedTodayCount,
            counts.FailedCount,
            retryQueue.Select(DocumentRetryQueueEntryDto.FromEntry).ToList(),
            statistics is null ? DocumentStatisticsSummaryDto.Empty : DocumentStatisticsSummaryDto.FromEntity(statistics));
    }
}
