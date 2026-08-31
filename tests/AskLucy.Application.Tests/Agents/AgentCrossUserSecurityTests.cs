using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.CancelAgentExecution;
using AskLucy.Application.Agents.Commands.PauseAgentExecution;
using AskLucy.Application.Agents.Queries.GetAgentExecution;
using AskLucy.Application.Agents.Queries.GetAgentExecutionSteps;
using AskLucy.Application.Agents.Queries.GetAgentToolCalls;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md FR-048/SC-010, Edge Cases ("What happens when a user attempts to view or control...
/// another user's execution? Access is denied and the attempt is recorded as a security event.") —
/// every execution-scoped handler denies access with 404-shaped <see cref="KeyNotFoundException"/>,
/// never a 403-shaped exception that would disclose the execution's existence.
/// </summary>
public sealed class AgentCrossUserSecurityTests
{
    private const string OwnerId = "user-1";
    private const string AttackerId = "user-2";

    [Fact]
    public async Task GetAgentExecutionQueryHandler_ShouldThrowNotFound_AndRecordCrossUserAccessAttempted_WhenTheExecutionBelongsToAnotherUser()
    {
        var execution = AgentExecution.Create(Guid.NewGuid(), Guid.NewGuid(), OwnerId, "Do something.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(execution.Id, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        var agentRepository = Substitute.For<IAgentRepository>();
        var auditLogRepository = Substitute.For<IAgentAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetAgentExecutionQueryHandler(executionRepository, agentRepository, auditLogRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new GetAgentExecutionQuery(execution.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        auditLogRepository.Received(1).Add(Arg.Is<AgentAuditLog>(log => log != null && log.Action == AgentAuditAction.CrossUserAccessAttempted && log.UserId == AttackerId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAgentExecutionQueryHandler_ShouldThrowNotFound_WithoutRecordingAnything_WhenTheExecutionGenuinelyDoesNotExist()
    {
        var missingId = Guid.NewGuid();
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        executionRepository.GetByIdForUserAsync(missingId, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        executionRepository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        var agentRepository = Substitute.For<IAgentRepository>();
        var auditLogRepository = Substitute.For<IAgentAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetAgentExecutionQueryHandler(executionRepository, agentRepository, auditLogRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new GetAgentExecutionQuery(missingId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        auditLogRepository.DidNotReceive().Add(Arg.Any<AgentAuditLog>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAgentExecutionStepsQueryHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetAgentExecutionStepsQueryHandler(executionRepository, currentUser);
        var act = () => handler.Handle(new GetAgentExecutionStepsQuery(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetAgentToolCallsQueryHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new GetAgentToolCallsQueryHandler(executionRepository, currentUser);
        var act = () => handler.Handle(new GetAgentToolCallsQuery(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PauseAgentExecutionCommandHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new PauseAgentExecutionCommandHandler(executionRepository, unitOfWork, currentUser);
        var act = () => handler.Handle(new PauseAgentExecutionCommand(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CancelAgentExecutionCommandHandler_ShouldThrowNotFound_ForAnotherUsersExecution()
    {
        var executionRepository = Substitute.For<IAgentExecutionRepository>();
        var executionId = Guid.NewGuid();
        executionRepository.GetByIdForUserAsync(executionId, AttackerId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);
        var notifier = Substitute.For<IAgentExecutionNotifier>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(AttackerId);

        var handler = new CancelAgentExecutionCommandHandler(executionRepository, notifier, unitOfWork, currentUser);
        var act = () => handler.Handle(new CancelAgentExecutionCommand(executionId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
