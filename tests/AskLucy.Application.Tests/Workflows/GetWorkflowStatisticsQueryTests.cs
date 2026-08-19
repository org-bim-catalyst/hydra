using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows;
using AskLucy.Application.Workflows.Queries.GetWorkflowStatistics;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// The Workflow Monitoring dashboard aggregate (spec.md User Story 8) — bucket counts, average
/// duration, failure rate, and usage/cost totals — is computed by <c>WorkflowExecutionRepository.GetStatisticsAsync</c>
/// via direct EF aggregate queries (mirrors AdminDashboardRepository, see WorkflowExecutionStatisticsTests
/// in AskLucy.Persistence.Tests for the actual bucket/averaging/failure-rate arithmetic against a real
/// database). This handler-level test only proves the query is scoped to the caller and passes the
/// repository's result through untouched.
/// </summary>
public sealed class GetWorkflowStatisticsQueryTests
{
    [Fact]
    public async Task Handle_ShouldReturnTheRepositorysStatistics_ScopedToTheCallingUser()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns("user-1");

        var expected = new WorkflowStatisticsDto(
            ActiveCount: 2, QueuedCount: 1, FailedCount: 3, CompletedCount: 5,
            AverageDurationSeconds: 12.5, FailureRate: 3d / 8d,
            TotalInputTokens: 100, TotalOutputTokens: 200, TotalEstimatedCost: 1.23m);
        executionRepository.GetStatisticsAsync("user-1", Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetWorkflowStatisticsQueryHandler(executionRepository, currentUser);
        var result = await handler.Handle(new GetWorkflowStatisticsQuery(), CancellationToken.None);

        result.Should().Be(expected);
        await executionRepository.Received(1).GetStatisticsAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns((string?)null);

        var handler = new GetWorkflowStatisticsQueryHandler(executionRepository, currentUser);
        var act = () => handler.Handle(new GetWorkflowStatisticsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
