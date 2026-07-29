using AskLucy.Application.Abstractions;
using AskLucy.Application.Admin;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Assembles the Admin Dashboard summary from live aggregate queries against the existing
/// Identity tables — no new persisted read model (research.md Topic 5).
/// </summary>
public sealed class AdminDashboardRepository(AskLucyDbContext dbContext) : IAdminDashboardRepository
{
    private const int TrendWindowDays = 30;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalUsers = await dbContext.Users.CountAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var lockedOutUsers = await dbContext.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > now, cancellationToken);
        var activeUsers = totalUsers - lockedOutUsers;

        var emailConfirmedUsers = await dbContext.Users.CountAsync(u => u.EmailConfirmed, cancellationToken);
        var emailPendingUsers = totalUsers - emailConfirmedUsers;

        var twoFactorEnabledUsers = await dbContext.Users.CountAsync(u => u.TwoFactorEnabled, cancellationToken);

        var newUsersLast30Days = await GetNewUsersLast30DaysAsync(cancellationToken);
        var roleDistribution = await GetRoleDistributionAsync(totalUsers, cancellationToken);

        return new DashboardSummaryDto(
            totalUsers,
            newUsersLast30Days,
            activeUsers,
            lockedOutUsers,
            emailConfirmedUsers,
            emailPendingUsers,
            twoFactorEnabledUsers,
            roleDistribution);
    }

    private async Task<IReadOnlyList<DailyUserCountDto>> GetNewUsersLast30DaysAsync(CancellationToken cancellationToken)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = todayUtc.AddDays(-(TrendWindowDays - 1));
        var windowStartUtc = windowStart.ToDateTime(TimeOnly.MinValue);

        var counted = await dbContext.Users
            .Where(u => u.CreatedAtUtc >= windowStartUtc)
            .GroupBy(u => u.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byDate = counted.ToDictionary(x => DateOnly.FromDateTime(x.Date), x => x.Count);

        // Zero-fill every day in the window, even ones with no signups, so the chart's x-axis
        // is a stable 30-point series rather than a sparse set of only the days with data.
        var series = new List<DailyUserCountDto>(TrendWindowDays);
        for (var day = windowStart; day <= todayUtc; day = day.AddDays(1))
        {
            series.Add(new DailyUserCountDto(day, byDate.GetValueOrDefault(day)));
        }

        return series;
    }

    private async Task<IReadOnlyList<RoleCountDto>> GetRoleDistributionAsync(int totalUsers, CancellationToken cancellationToken)
    {
        var privilegedCounts = await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            group userRole by role.Name into g
            select new { RoleName = g.Key!, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var privilegedTotal = privilegedCounts.Sum(r => r.Count);
        var regularCount = totalUsers - privilegedTotal;

        var distribution = privilegedCounts
            .Select(r => new RoleCountDto(r.RoleName, r.Count))
            .OrderByDescending(r => r.UserCount)
            .ToList();

        distribution.Add(new RoleCountDto("Regular", regularCount));

        return distribution;
    }
}
