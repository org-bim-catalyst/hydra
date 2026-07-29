using AskLucy.Application.Abstractions;
using AskLucy.Application.Admin;
using AskLucy.Application.Admin.Queries.GetDashboardSummary;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Admin;

public sealed class GetAdminDashboardSummaryQueryHandlerTests
{
    private readonly IAdminDashboardRepository _repository = Substitute.For<IAdminDashboardRepository>();
    private readonly GetAdminDashboardSummaryQueryHandler _handler;

    public GetAdminDashboardSummaryQueryHandlerTests() => _handler = new GetAdminDashboardSummaryQueryHandler(_repository);

    [Fact]
    public async Task Handle_ShouldReturnRepositorySummary_Unmodified()
    {
        var summary = new DashboardSummaryDto(
            TotalUsers: 42,
            NewUsersLast30Days: [new DailyUserCountDto(new DateOnly(2026, 7, 28), 2)],
            ActiveUsers: 39,
            LockedOutUsers: 3,
            EmailConfirmedUsers: 40,
            EmailPendingUsers: 2,
            TwoFactorEnabledUsers: 11,
            RoleDistribution: [new RoleCountDto("Super User", 1), new RoleCountDto("Administrator", 2), new RoleCountDto("Regular", 39)]);

        _repository.GetSummaryAsync(Arg.Any<CancellationToken>()).Returns(summary);

        var result = await _handler.Handle(new GetAdminDashboardSummaryQuery(), CancellationToken.None);

        result.Should().Be(summary);
    }
}
