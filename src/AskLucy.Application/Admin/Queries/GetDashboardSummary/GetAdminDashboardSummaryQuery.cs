using MediatR;

namespace AskLucy.Application.Admin.Queries.GetDashboardSummary;

public sealed record GetAdminDashboardSummaryQuery : IRequest<DashboardSummaryDto>;
