using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseDashboardSummary;

/// <summary>Cached per-user, 60s TTL (research.md Decision 7, FR-035) — every mutating handler that changes a reported count invalidates via <see cref="KnowledgeBaseDashboardSummaryCache"/>.</summary>
public sealed class GetKnowledgeBaseDashboardSummaryQueryHandler(
    IKnowledgeBaseRepository repository,
    KnowledgeBaseDashboardSummaryCache cache,
    TimeProvider timeProvider,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetKnowledgeBaseDashboardSummaryQuery, KnowledgeBaseDashboardSummaryDto>
{
    public async Task<KnowledgeBaseDashboardSummaryDto> Handle(GetKnowledgeBaseDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (cache.TryGet(userId, out var cached))
        {
            return cached;
        }

        var recentSinceUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-7);
        var summary = await repository.GetDashboardSummaryAsync(userId, recentSinceUtc, cancellationToken);
        cache.Set(userId, summary);

        return summary;
    }
}
