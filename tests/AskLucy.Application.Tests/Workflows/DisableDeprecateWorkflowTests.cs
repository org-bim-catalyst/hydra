using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Commands.DeprecateWorkflow;
using AskLucy.Application.Workflows.Commands.DisableWorkflow;
using AskLucy.Application.Workflows.Commands.EnableWorkflow;
using AskLucy.Application.Workflows.Commands.StartWorkflowExecution;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md FR-002/Acceptance Scenario 9.3 — disabling a workflow stops event-trigger dispatch
/// (covered directly against <see cref="IWorkflowRepository.ListPublishedEventDrivenAsync"/>'s own
/// Published-only filter, and defense-in-depth in <c>WorkflowEventTriggerHandlerTests</c>); a
/// manual start still succeeds against a Disabled workflow. Deprecating is one-way and blocks BOTH
/// manual and event-triggered starts (<see cref="Workflow.Deprecate"/>'s own doc comment).
/// </summary>
public sealed class DisableDeprecateWorkflowTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private (Workflow Workflow, WorkflowVersion Version) SetUpPublishedWorkflow()
    {
        _currentUser.UserId.Returns(OwnerId);
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                new WorkflowNodeSpec("end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
            ],
            [new WorkflowConnectionSpec("start", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        _workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        return (workflow, version);
    }

    [Fact]
    public async Task DisableWorkflowCommandHandler_ShouldTransitionToDisabled()
    {
        var (workflow, _) = SetUpPublishedWorkflow();
        var handler = new DisableWorkflowCommandHandler(_workflowRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new DisableWorkflowCommand(workflow.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(WorkflowStatus.Disabled));
    }

    [Fact]
    public async Task EnableWorkflowCommandHandler_ShouldReverseDisable()
    {
        var (workflow, _) = SetUpPublishedWorkflow();
        workflow.Disable(OwnerId);
        var handler = new EnableWorkflowCommandHandler(_workflowRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new EnableWorkflowCommand(workflow.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(WorkflowStatus.Published));
    }

    [Fact]
    public async Task DeprecateWorkflowCommandHandler_ShouldTransitionToDeprecated()
    {
        var (workflow, _) = SetUpPublishedWorkflow();
        var handler = new DeprecateWorkflowCommandHandler(_workflowRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new DeprecateWorkflowCommand(workflow.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(WorkflowStatus.Deprecated));
    }

    [Fact]
    public async Task StartWorkflowExecutionCommandHandler_ShouldStillSucceed_AgainstADisabledWorkflow()
    {
        var (workflow, version) = SetUpPublishedWorkflow();
        workflow.Disable(OwnerId);
        _workflowRepository.GetVersionAsync(workflow.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        var handler = CreateStartHandler();

        var result = await handler.Handle(new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        result.Should().NotBeNull(); // Disable only stops event-trigger dispatch — manual starts remain allowed.
    }

    [Fact]
    public async Task StartWorkflowExecutionCommandHandler_ShouldThrow_AgainstADeprecatedWorkflow()
    {
        var (workflow, version) = SetUpPublishedWorkflow();
        workflow.Deprecate(OwnerId);
        _workflowRepository.GetVersionAsync(workflow.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        var handler = CreateStartHandler();

        var act = () => handler.Handle(new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    private StartWorkflowExecutionCommandHandler CreateStartHandler()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        policyRepository.GetUserExecutionLimitAsync(OwnerId, Arg.Any<CancellationToken>()).Returns((WorkflowUserExecutionLimit?)null);
        executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        return new StartWorkflowExecutionCommandHandler(
            _workflowRepository, executionRepository, policyRepository, runner, auditLogRepository,
            Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()), _unitOfWork, _currentUser);
    }
}
