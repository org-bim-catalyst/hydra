using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;
using AskLucy.Application.Documents.Queries.GetOrganizationDashboardSummary;
using AskLucy.Application.Tests.KnowledgeBases;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T116 — <c>GetDocumentDashboardSummary</c> is scoped to the caller only; <c>GetOrganizationDashboardSummary</c> aggregates organization-wide (role-gating itself is a controller-level policy, covered by <c>DashboardAuthorizationTests</c> in Web.Tests).</summary>
public sealed class DashboardSummaryTests
{
    private readonly IDocumentProcessingJobRepository _jobRepository = Substitute.For<IDocumentProcessingJobRepository>();
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-08-04T12:00:00Z", CultureInfo.InvariantCulture));

    [Fact]
    public async Task GetDocumentDashboardSummary_ShouldQueryOnlyTheCallersOwnScope()
    {
        _currentUser.UserId.Returns("user-1");
        _jobRepository.GetDashboardCountsAsync("user-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentDashboardCounts(3, 2, 14, 1));
        _jobRepository.GetRetryQueueAsync("user-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentRetryQueueEntry(Guid.CreateVersion7(), "bad.pdf", "Corrupted file.")]);
        _statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.User, "user-1", Arg.Any<CancellationToken>())
            .Returns(DocumentStatistics.CreateForUser("user-1", "system"));

        var handler = new GetDocumentDashboardSummaryQueryHandler(_jobRepository, _statisticsRepository, _timeProvider, _currentUser);
        var result = await handler.Handle(new GetDocumentDashboardSummaryQuery(), CancellationToken.None);

        result.QueueDepth.Should().Be(3);
        result.InProgressCount.Should().Be(2);
        result.CompletedTodayCount.Should().Be(14);
        result.FailedCount.Should().Be(1);
        result.RetryQueue.Should().ContainSingle(e => e.FileName == "bad.pdf");

        // Never queried organization-wide (null owner) for a per-user request.
        await _jobRepository.DidNotReceive().GetDashboardCountsAsync(null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _statisticsRepository.DidNotReceive().GetByScopeAsync(DocumentStatisticsScope.Organization, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDocumentDashboardSummary_ShouldReturnEmptyStatistics_WhenTheRecomputeJobHasNeverRunForThisUser()
    {
        _currentUser.UserId.Returns("new-user");
        _jobRepository.GetDashboardCountsAsync("new-user", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentDashboardCounts(0, 0, 0, 0));
        _jobRepository.GetRetryQueueAsync("new-user", Arg.Any<CancellationToken>()).Returns([]);
        _statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.User, "new-user", Arg.Any<CancellationToken>())
            .Returns((DocumentStatistics?)null);

        var handler = new GetDocumentDashboardSummaryQueryHandler(_jobRepository, _statisticsRepository, _timeProvider, _currentUser);
        var result = await handler.Handle(new GetDocumentDashboardSummaryQuery(), CancellationToken.None);

        result.Statistics.Should().Be(DocumentStatisticsSummaryDto.Empty);
    }

    [Fact]
    public async Task GetOrganizationDashboardSummary_ShouldQueryOrganizationWide_NotScopedToAnyOneOwner()
    {
        _jobRepository.GetDashboardCountsAsync(null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentDashboardCounts(10, 5, 40, 3));
        _jobRepository.GetRetryQueueAsync(null, Arg.Any<CancellationToken>()).Returns([]);
        _statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.Organization, null, Arg.Any<CancellationToken>())
            .Returns(DocumentStatistics.CreateForOrganization("system"));

        var handler = new GetOrganizationDashboardSummaryQueryHandler(_jobRepository, _statisticsRepository, _timeProvider);
        var result = await handler.Handle(new GetOrganizationDashboardSummaryQuery(), CancellationToken.None);

        result.QueueDepth.Should().Be(10);
        result.FailedCount.Should().Be(3);
    }
}
