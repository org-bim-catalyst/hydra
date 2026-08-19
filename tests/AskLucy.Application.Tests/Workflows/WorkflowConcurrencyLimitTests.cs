using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Commands.SetWorkflowUserExecutionLimit;
using AskLucy.Application.Workflows.Commands.StartWorkflowExecution;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md FR-069/FR-070 (Polish phase T196-T200) — an Administrator/Super User can override a
/// specific user's concurrent-execution cap (<see cref="WorkflowUserExecutionLimit"/>), and that
/// override — not just <see cref="WorkflowRuntimeOptions.DefaultMaxConcurrentExecutions"/> — is
/// what both the manual-start path (<see cref="StartWorkflowExecutionCommandHandler"/>) and the
/// event-trigger dispatch path (<see cref="WorkflowEventTriggerHandler"/>) actually enforce.
/// </summary>
public sealed class WorkflowConcurrencyLimitTests
{
    private const string UserId = "user-1";
    private const string AdminId = "admin-1";

    [Fact]
    public async Task SetWorkflowUserExecutionLimitCommandHandler_ShouldCreateANewLimit_WhenNoneExists()
    {
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminId);
        policyRepository.GetUserExecutionLimitAsync(UserId, Arg.Any<CancellationToken>()).Returns((WorkflowUserExecutionLimit?)null);

        var handler = new SetWorkflowUserExecutionLimitCommandHandler(policyRepository, unitOfWork, currentUser);
        var result = await handler.Handle(new SetWorkflowUserExecutionLimitCommand(UserId, 10), CancellationToken.None);

        result.Should().Be(new WorkflowUserExecutionLimitDto(UserId, 10));
        policyRepository.Received(1).AddUserExecutionLimit(Arg.Is<WorkflowUserExecutionLimit>(l => l.UserId == UserId && l.MaxConcurrentExecutions == 10 && l.SetByUserId == AdminId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetWorkflowUserExecutionLimitCommandHandler_ShouldUpdateTheExistingLimit_WhenOneAlreadyExists()
    {
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AdminId);
        var existing = WorkflowUserExecutionLimit.Create(UserId, 3, "previous-admin");
        policyRepository.GetUserExecutionLimitAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new SetWorkflowUserExecutionLimitCommandHandler(policyRepository, unitOfWork, currentUser);
        await handler.Handle(new SetWorkflowUserExecutionLimitCommand(UserId, 20), CancellationToken.None);

        existing.MaxConcurrentExecutions.Should().Be(20);
        policyRepository.DidNotReceive().AddUserExecutionLimit(Arg.Any<WorkflowUserExecutionLimit>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartWorkflowExecutionCommandHandler_ShouldRejectBelowTheDefault_WhenAStricterPerUserLimitApplies()
    {
        var (workflow, version) = SetUpPublishedWorkflow(out var workflowRepository);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserId);
        workflowRepository.GetVersionAsync(workflow.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        // Default allows 10, but this user has a stricter override of 1, and already has 1 active.
        policyRepository.GetUserExecutionLimitAsync(UserId, Arg.Any<CancellationToken>()).Returns(WorkflowUserExecutionLimit.Create(UserId, 1, AdminId));
        executionRepository.CountActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(1);

        var handler = new StartWorkflowExecutionCommandHandler(
            workflowRepository, executionRepository, policyRepository, runner, auditLogRepository,
            Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions { DefaultMaxConcurrentExecutions = 10 }), unitOfWork, currentUser);

        var act = () => handler.Handle(new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<WorkflowConcurrencyLimitExceededException>();
        executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
    }

    [Fact]
    public async Task StartWorkflowExecutionCommandHandler_ShouldAllowAboveTheDefault_WhenALooserPerUserLimitApplies()
    {
        var (workflow, version) = SetUpPublishedWorkflow(out var workflowRepository);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserId);
        workflowRepository.GetVersionAsync(workflow.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        // Default only allows 3, but this user has a looser override of 10, and already has 5 active.
        policyRepository.GetUserExecutionLimitAsync(UserId, Arg.Any<CancellationToken>()).Returns(WorkflowUserExecutionLimit.Create(UserId, 10, AdminId));
        executionRepository.CountActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(5);

        var handler = new StartWorkflowExecutionCommandHandler(
            workflowRepository, executionRepository, policyRepository, runner, auditLogRepository,
            Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions { DefaultMaxConcurrentExecutions = 3 }), unitOfWork, currentUser);

        var result = await handler.Handle(new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        result.Should().NotBeNull();
        executionRepository.Received(1).Add(Arg.Any<WorkflowExecution>());
    }

    [Fact]
    public async Task WorkflowEventTriggerHandler_ShouldRespectThePerUserOverride_NotJustTheDefault()
    {
        var knowledgeBaseId = Guid.NewGuid();
        var workflow = Workflow.Create(UserId, "Triggered Workflow", null, WorkflowType.EventDriven, UserId);
        var version = workflow.Publish(
            [
                new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                new WorkflowNodeSpec("end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
            ],
            [new WorkflowConnectionSpec("start", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, UserId);
        workflow.SetEventTriggerConfiguration($$"""{"eventType":"KnowledgeBaseUpdated","knowledgeBaseId":"{{knowledgeBaseId}}"}""", UserId);

        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.ListPublishedEventDrivenAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(Workflow, WorkflowVersion)>)[(workflow, version)]);
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var policyRepository = Substitute.For<IWorkflowPolicyRepository>();
        var knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
        knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId, Arg.Any<CancellationToken>()).Returns(KnowledgeBase.Create("KB", UserId, UserId));
        var runner = Substitute.For<IWorkflowExecutionRunner>();
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        // Default allows 10, but this owner has a stricter override of 1, and already has 1 active.
        policyRepository.GetUserExecutionLimitAsync(UserId, Arg.Any<CancellationToken>()).Returns(WorkflowUserExecutionLimit.Create(UserId, 1, AdminId));
        executionRepository.CountActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(1);

        var handler = new WorkflowEventTriggerHandler(
            workflowRepository, executionRepository, policyRepository, knowledgeBaseRepository, runner, auditLogRepository,
            Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions { DefaultMaxConcurrentExecutions = 10 }), unitOfWork,
            Substitute.For<ILogger<WorkflowEventTriggerHandler>>());

        await handler.Handle(new KnowledgeBaseUpdatedNotification(knowledgeBaseId, "someone-else"), CancellationToken.None);

        executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static (Workflow Workflow, WorkflowVersion Version) SetUpPublishedWorkflow(out IWorkflowRepository workflowRepository)
    {
        var workflow = Workflow.Create(UserId, "My Workflow", null, WorkflowType.Manual, UserId);
        var node = new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([node], [], [], "{}", "{}", "{}", "{}", "{}", null, UserId);

        workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetByIdForOwnerAsync(workflow.Id, UserId, Arg.Any<CancellationToken>()).Returns(workflow);

        return (workflow, version);
    }
}
