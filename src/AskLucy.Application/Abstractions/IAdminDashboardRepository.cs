using AskLucy.Application.Admin;

namespace AskLucy.Application.Abstractions;

/// <summary>Read-only aggregate access for the Admin Dashboard (specs/001-admin-dashboard).</summary>
public interface IAdminDashboardRepository
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
