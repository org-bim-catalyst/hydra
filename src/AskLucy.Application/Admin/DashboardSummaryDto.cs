namespace AskLucy.Application.Admin;

/// <summary>
/// Admin Dashboard aggregate view (specs/001-admin-dashboard FR-001 through FR-007) —
/// computed on demand from existing <c>ApplicationUser</c>/role data, never persisted
/// (data-model.md &#167; DashboardSummaryDto).
/// </summary>
public sealed record DashboardSummaryDto(
    int TotalUsers,
    IReadOnlyList<DailyUserCountDto> NewUsersLast30Days,
    int ActiveUsers,
    int LockedOutUsers,
    int EmailConfirmedUsers,
    int EmailPendingUsers,
    int TwoFactorEnabledUsers,
    IReadOnlyList<RoleCountDto> RoleDistribution);

public sealed record DailyUserCountDto(DateOnly Date, int NewUsers);

public sealed record RoleCountDto(string RoleName, int UserCount);
