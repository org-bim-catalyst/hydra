using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Commands.CancelWorkflowExecution;
using AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecution;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecutionNodes;
using AskLucy.Application.Workflows.Queries.GetWorkflowExecutionUsage;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md FR-059/SC-008, Edge Cases ("access to another user's workflow/execution is denied and the
/// attempt is recorded as a security event") — every execution-scoped handler denies access with a
/// 404-shaped <see cref="KeyNotFoundException"/>, never a 403-shaped exception that would disclose the
/// execution's existence. Mirrors AgentCrossUserSecurityTests (spec 020 precedent) exactly.
/// </summary>
public sealed class WorkflowCrossUserSecurityTests
{
    private const string OwnerId = "user-1";
    private const string AttackerId = "user-2";

    private static WorkflowExecution CreateExecution() =>
        WorkflowExecution.Create(Guid.NewGuid(), Guid.NewGuid(), OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);

    [Fact]
    public async Task GetWorkflowExecutionQueryHandler_ShouldThrowNotFound_AndRecordCrossUserAccessAttempted_WhenTheExecutionBelongsToAnotherUser()
    {
        var execution = CreateExecution();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetWorkflowExecutionQueryHandler(executionRepository, auditLogRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new GetWorkflowExecutionQuery(execution.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        auditLogRepository.Received(1).Add(Arg.Is<WorkflowAuditLog>(log => log != null && log.Action == WorkflowAuditAction.CrossUserAccessAttempted && log.ActorUserId == AttackerId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflowExecutionQueryHandler_ShouldThrowNotFound_WithoutRecordingAnything_WhenTheExecutionGenuinelyDoesNotExist()
    {
        var missingId = Guid.NewGuid();
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        executionRepository.GetByIdForUserAsync(missingId, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        executionRepository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetWorkflowExecutionQueryHandler(executionRepository, auditLogRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new GetWorkflowExecutionQuery(missingId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        auditLogRepository.DidNotReceive().Add(Arg.Any<WorkflowAuditLog>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflowExecutionNodesQueryHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetWorkflowExecutionNodesQueryHandler(executionRepository, currentUser);
        var act = () => handler.Handle(new GetWorkflowExecutionNodesQuery(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetWorkflowExecutionUsageQueryHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetWorkflowExecutionUsageQueryHandler(executionRepository, currentUser);
        var act = () => handler.Handle(new GetWorkflowExecutionUsageQuery(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PauseWorkflowExecutionCommandHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new PauseWorkflowExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        var act = () => handler.Handle(new PauseWorkflowExecutionCommand(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CancelWorkflowExecutionCommandHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IWorkflowExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);
        var notifier = Substitute.For<IWorkflowExecutionNotifier>();
        var auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new CancelWorkflowExecutionCommandHandler(executionRepository, notifier, auditLogRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new CancelWorkflowExecutionCommand(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
