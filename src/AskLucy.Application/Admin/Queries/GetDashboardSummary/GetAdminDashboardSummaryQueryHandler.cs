using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Admin.Queries.GetDashboardSummary;

/// <summary>Admin-only (FR-001, enforced at the endpoint) — assembles the dashboard from live aggregates, never cached (research.md Topic 5).</summary>
public sealed class GetAdminDashboardSummaryQueryHandler(IAdminDashboardRepository repository)
    : IRequestHandler<GetAdminDashboardSummaryQuery, DashboardSummaryDto>
{
    public Task<DashboardSummaryDto> Handle(GetAdminDashboardSummaryQuery request, CancellationToken cancellationToken) =>
        repository.GetSummaryAsync(cancellationToken);
}
