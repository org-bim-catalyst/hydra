using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetOrganizationDashboardSummary;

/// <summary>FR-045a, US6 AC6 — the same shape as the per-user dashboard, but every count/query is scoped to no owner (organization-wide) rather than the caller.</summary>
public sealed class GetOrganizationDashboardSummaryQueryHandler(
    IDocumentProcessingJobRepository jobRepository,
    IDocumentStatisticsRepository statisticsRepository,
    TimeProvider timeProvider) : IRequestHandler<GetOrganizationDashboardSummaryQuery, DocumentDashboardSummaryDto>
{
    public async Task<DocumentDashboardSummaryDto> Handle(GetOrganizationDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var todayStartUtc = timeProvider.GetUtcNow().UtcDateTime.Date;

        var counts = await jobRepository.GetDashboardCountsAsync(null, todayStartUtc, cancellationToken);
        var retryQueue = await jobRepository.GetRetryQueueAsync(null, cancellationToken);
        var statistics = await statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.Organization, null, cancellationToken);

        return new DocumentDashboardSummaryDto(
            counts.QueueDepth,
            counts.InProgressCount,
            counts.CompletedTodayCount,
            counts.FailedCount,
            retryQueue.Select(DocumentRetryQueueEntryDto.FromEntry).ToList(),
            statistics is null ? DocumentStatisticsSummaryDto.Empty : DocumentStatisticsSummaryDto.FromEntity(statistics));
    }
}
