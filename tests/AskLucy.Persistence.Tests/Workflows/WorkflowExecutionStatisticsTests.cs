using AskLucy.Domain.Workflows;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Workflows;

/// <summary>
/// Proves <see cref="WorkflowExecutionRepository.GetStatisticsAsync"/>'s Workflow Monitoring
/// dashboard aggregate (spec.md User Story 8) — bucket counts, failure rate, and aggregate
/// usage/cost — against real seeded rows in a real database (see GetWorkflowStatisticsQueryTests
/// in AskLucy.Application.Tests for the handler-level delegation/authorization test). Every
/// seeded execution uses a unique owner id so this test is isolated from other tests sharing the
/// same collection-fixture database.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class WorkflowExecutionStatisticsTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task GetStatisticsAsync_ShouldBucketCountsAndSumUsageAndCost_Correctly()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerId = $"stats-owner-{suffix}";

        Guid workflowId, versionId;

        await using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Users.AddAsync(PersistenceTestFixture.CreateTestUser(ownerId), TestContext.Current.CancellationToken);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var workflow = Workflow.Create(ownerId, $"Stats Workflow {suffix}", null, WorkflowType.Manual, ownerId);
            var version = workflow.Publish(
                [
                    new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                    new WorkflowNodeSpec("end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                ],
                [new WorkflowConnectionSpec("start", "end", null, null)],
                [], "{}", "{}", "{}", "{}", "{}", null, ownerId);

            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            workflowId = workflow.Id;
            versionId = version.Id;

            WorkflowExecution NewExecution() => WorkflowExecution.Create(workflowId, versionId, ownerId, WorkflowExecutionTriggerType.Manual, null, "{}", ownerId);

            var running = NewExecution();
            running.Start();

            var paused = NewExecution();
            paused.Start();
            paused.Pause();

            var queued = NewExecution(); // stays Queued

            var failed = NewExecution();
            failed.Start();
            failed.Fail("boom");

            var cancelled = NewExecution();
            cancelled.Start();
            cancelled.Cancel("cancelled by user");

            var timedOut = NewExecution();
            timedOut.Start();
            timedOut.TimeOut("exceeded budget");

            var completed1 = NewExecution();
            completed1.Start();
            completed1.Complete(null);
            var usage1 = WorkflowExecutionUsage.CreateEmpty(completed1.Id);
            usage1.Accumulate(50, 100, 0, 0);
            completed1.SetUsage(usage1);
            completed1.SetCost(WorkflowExecutionCost.Create(completed1.Id, 0.75m, "USD"));

            var completed2 = NewExecution();
            completed2.Start();
            completed2.Complete(null);
            var usage2 = WorkflowExecutionUsage.CreateEmpty(completed2.Id);
            usage2.Accumulate(30, 20, 0, 0);
            completed2.SetUsage(usage2);
            completed2.SetCost(WorkflowExecutionCost.Create(completed2.Id, 0.25m, "USD"));

            dbContext.WorkflowExecutions.AddRange(running, paused, queued, failed, cancelled, timedOut, completed1, completed2);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new WorkflowExecutionRepository(readContext);

        var stats = await repository.GetStatisticsAsync(ownerId, CancellationToken.None);

        stats.ActiveCount.Should().Be(2); // Running + Paused
        stats.QueuedCount.Should().Be(1);
        stats.FailedCount.Should().Be(3); // Failed + Cancelled + TimedOut
        stats.CompletedCount.Should().Be(2);
        stats.FailureRate.Should().BeApproximately(3d / 5d, 0.0001); // 3 failed-like of 5 decided (3 failed-like + 2 completed)
        stats.AverageDurationSeconds.Should().NotBeNull();
        stats.AverageDurationSeconds!.Value.Should().BeGreaterThanOrEqualTo(0);
        stats.TotalInputTokens.Should().Be(80);
        stats.TotalOutputTokens.Should().Be(120);
        stats.TotalEstimatedCost.Should().Be(1.00m);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnZeroFailureRateAndNullAverageDuration_WhenTheUserHasNoTerminalExecutions()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerId = $"stats-empty-owner-{suffix}";

        await using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Users.AddAsync(PersistenceTestFixture.CreateTestUser(ownerId), TestContext.Current.CancellationToken);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var workflow = Workflow.Create(ownerId, $"Empty Stats Workflow {suffix}", null, WorkflowType.Manual, ownerId);
            var version = workflow.Publish(
                [
                    new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                    new WorkflowNodeSpec("end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                ],
                [new WorkflowConnectionSpec("start", "end", null, null)],
                [], "{}", "{}", "{}", "{}", "{}", null, ownerId);

            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var queued = WorkflowExecution.Create(workflow.Id, version.Id, ownerId, WorkflowExecutionTriggerType.Manual, null, "{}", ownerId);
            dbContext.WorkflowExecutions.Add(queued);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new WorkflowExecutionRepository(readContext);

        var stats = await repository.GetStatisticsAsync(ownerId, CancellationToken.None);

        stats.QueuedCount.Should().Be(1);
        stats.FailedCount.Should().Be(0);
        stats.CompletedCount.Should().Be(0);
        stats.FailureRate.Should().Be(0);
        stats.AverageDurationSeconds.Should().BeNull();
        stats.TotalInputTokens.Should().Be(0);
        stats.TotalOutputTokens.Should().Be(0);
        stats.TotalEstimatedCost.Should().Be(0m);
    }
}
