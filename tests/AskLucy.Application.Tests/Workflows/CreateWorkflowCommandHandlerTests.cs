using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Commands.CreateWorkflow;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class CreateWorkflowCommandHandlerTests
{
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowAuditLogRepository _auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreateWorkflowCommandHandler CreateHandler() => new(_workflowRepository, _auditLogRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldCreateAWorkflowOwnedByTheCaller_InDraftStatus()
    {
        _currentUser.UserId.Returns("user-1");
        _workflowRepository.ExistsWithNameForOwnerAsync("user-1", "My Workflow", null, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(new CreateWorkflowCommand("My Workflow", "desc", WorkflowType.Manual), CancellationToken.None);

        result.Name.Should().Be("My Workflow");
        result.Status.Should().Be(nameof(WorkflowStatus.Draft));
        _workflowRepository.Received(1).Add(Arg.Is<Workflow>(w => w != null && w.OwnerId == "user-1" && w.Status == WorkflowStatus.Draft));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowDuplicateResource_WhenNameAlreadyExistsForOwner()
    {
        _currentUser.UserId.Returns("user-1");
        _workflowRepository.ExistsWithNameForOwnerAsync("user-1", "My Workflow", null, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new CreateWorkflowCommand("My Workflow", null, WorkflowType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateResourceException>();
        _workflowRepository.DidNotReceive().Add(Arg.Any<Workflow>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new CreateWorkflowCommand("My Workflow", null, WorkflowType.Manual), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
