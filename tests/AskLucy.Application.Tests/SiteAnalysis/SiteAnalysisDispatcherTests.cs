using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>research.md D12 — dispatch bypasses the ownership-guarded command and always runs as the real signed-in user (tasks.md rule 3). specs/057-site-analysis-agent tasks.md T038.</summary>
public sealed class SiteAnalysisDispatcherTests
{
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SiteAnalysisDispatcher BuildDispatcher() => new(_workflowRepository, _executionRepository, _backgroundJobClient, _unitOfWork);

    private static Workflow BuildPublishedWorkflow()
    {
        var workflow = Workflow.CreateSystemProvisioned(SiteAnalysisDispatcher.WorkflowSystemKey, "Site Analysis", null, WorkflowType.EventDriven, "actor");
        WorkflowNodeSpec Node(string key, WorkflowNodeType type) =>
            new(key, type, key, null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, "actor");

        return workflow;
    }

    [Fact]
    public async Task DispatchAsync_ShouldRunAsTheRealSignedInUser_NeverASystemId()
    {
        var workflow = BuildPublishedWorkflow();
        _workflowRepository.GetBySystemKeyAsync(SiteAnalysisDispatcher.WorkflowSystemKey, Arg.Any<CancellationToken>()).Returns(workflow);

        WorkflowExecution? captured = null;
        _executionRepository.When(r => r.Add(Arg.Any<WorkflowExecution>())).Do(call => captured = call.Arg<WorkflowExecution>());

        var dispatcher = BuildDispatcher();
        var executionId = await dispatcher.DispatchAsync(Guid.NewGuid(), "real-user-1", "Al Barsha South", "Dubai, UAE", 25.09, 55.20);

        captured.Should().NotBeNull();
        captured!.RunByUserId.Should().Be("real-user-1");
        captured.RunByUserId.Should().NotBe("system");
        executionId.Should().Be(captured.Id);
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(IWorkflowExecutionRunner)
                && j.Method.Name == nameof(IWorkflowExecutionRunner.RunJobAsync)
                && (Guid)j.Args[0] == captured.Id),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrow_WhenTheSystemWorkflowHasNotBeenProvisioned()
    {
        _workflowRepository.GetBySystemKeyAsync(SiteAnalysisDispatcher.WorkflowSystemKey, Arg.Any<CancellationToken>()).Returns((Workflow?)null);

        var dispatcher = BuildDispatcher();
        var act = () => dispatcher.DispatchAsync(Guid.NewGuid(), "user-1", "Site", "Location", 0, 0);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
