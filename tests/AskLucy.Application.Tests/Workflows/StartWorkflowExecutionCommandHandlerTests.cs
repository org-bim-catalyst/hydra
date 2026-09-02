using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Commands.StartWorkflowExecution;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class StartWorkflowExecutionCommandHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IWorkflowExecutionRunner _runner = Substitute.For<IWorkflowExecutionRunner>();
    private readonly IWorkflowAuditLogRepository _auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private StartWorkflowExecutionCommandHandler CreateHandler(int defaultMaxConcurrentExecutions = 3) => new(
        _workflowRepository, _executionRepository, _policyRepository, _runner, _auditLogRepository,
        Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions { DefaultMaxConcurrentExecutions = defaultMaxConcurrentExecutions }), _unitOfWork, _currentUser);

    private (Workflow Workflow, WorkflowVersion Version) SetUpPublishedWorkflow()
    {
        _currentUser.UserId.Returns(OwnerId);
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var node = new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([node], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        _workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        _workflowRepository.GetVersionAsync(workflow.Id, 1, Arg.Any<CancellationToken>()).Returns(version);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        return (workflow, version);
    }

    [Fact]
    public async Task Handle_ShouldCreateAndEnqueueAnExecution_AgainstThePublishedVersion()
    {
        var (workflow, _) = SetUpPublishedWorkflow();

        var result = await CreateHandler().Handle(
            new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        result.Status.Should().Be(nameof(WorkflowExecutionStatus.Queued));
        _executionRepository.Received(1).Add(Arg.Is<WorkflowExecution>(e => e != null && e.RunByUserId == OwnerId));
        await _runner.Received(1).EnqueueAsync(result.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowConcurrencyLimitExceeded_WhenAtCap()
    {
        var (workflow, _) = SetUpPublishedWorkflow();
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(3);

        var act = () => CreateHandler(defaultMaxConcurrentExecutions: 3).Handle(
            new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<WorkflowConcurrencyLimitExceededException>();
        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowDomainRuleViolation_WhenTheWorkflowHasNoPublishedVersion()
    {
        _currentUser.UserId.Returns(OwnerId);
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        _workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        var act = () => CreateHandler().Handle(
            new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
