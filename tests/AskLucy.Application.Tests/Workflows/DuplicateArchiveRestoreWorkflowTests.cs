using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Commands.ArchiveWorkflow;
using AskLucy.Application.Workflows.Commands.DeleteWorkflow;
using AskLucy.Application.Workflows.Commands.DuplicateWorkflow;
using AskLucy.Application.Workflows.Commands.RestoreWorkflow;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>T097 — duplicate/archive/restore/delete lifecycle actions (spec.md User Story 3).</summary>
public sealed class DuplicateArchiveRestoreWorkflowTests
{
    private const string OwnerId = "user-1";

    private static Workflow CreatePublishedWorkflow()
    {
        var workflow = Workflow.Create(OwnerId, "Original Workflow", "Does things.", WorkflowType.Manual, OwnerId);
        workflow.UpdateDraft(workflow.Name, workflow.Description, "{\"nodes\":[],\"connections\":[],\"variables\":[]}", OwnerId);

        var start = new WorkflowNodeSpec(
            "start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var end = new WorkflowNodeSpec(
            "end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        workflow.Publish(
            [start, end], [new WorkflowConnectionSpec("start", "end", null, null)], [],
            "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        return workflow;
    }

    [Fact]
    public async Task DuplicateWorkflowCommandHandler_ShouldCopyTheDraftOnly_AsANewDraftWorkflow()
    {
        var workflow = CreatePublishedWorkflow();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new DuplicateWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser);
        var result = await handler.Handle(new DuplicateWorkflowCommand(workflow.Id), CancellationToken.None);

        result.Id.Should().NotBe(workflow.Id);
        result.Name.Should().Be("Original Workflow (Copy)");
        result.Status.Should().Be(nameof(WorkflowStatus.Draft));
        result.PublishedVersionNumber.Should().BeNull();
        result.DraftDefinitionJson.Should().Be(workflow.DraftDefinitionJson);
        workflowRepository.Received(1).Add(Arg.Is<Workflow>(w => w != null && w.Id != workflow.Id));
    }

    [Fact]
    public async Task ArchiveThenRestoreWorkflowCommandHandlers_ShouldReturnToThePreArchiveStatus()
    {
        var workflow = CreatePublishedWorkflow();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var archiveHandler = new ArchiveWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser);
        var archived = await archiveHandler.Handle(new ArchiveWorkflowCommand(workflow.Id), CancellationToken.None);
        archived.Status.Should().Be(nameof(WorkflowStatus.Archived));

        var restoreHandler = new RestoreWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser);
        var restored = await restoreHandler.Handle(new RestoreWorkflowCommand(workflow.Id), CancellationToken.None);
        restored.Status.Should().Be(nameof(WorkflowStatus.Published));
    }

    [Fact]
    public async Task DeleteWorkflowCommandHandler_ShouldSoftDeleteOnly_NeverTouchingVersionsOrExecutions()
    {
        var workflow = CreatePublishedWorkflow();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);

        var handler = new DeleteWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser);
        await handler.Handle(new DeleteWorkflowCommand(workflow.Id), CancellationToken.None);

        workflow.DeletedAtUtc.Should().NotBeNull();
        workflow.DeletedBy.Should().Be(OwnerId);
        workflow.PublishedVersionNumber.Should().Be(1);
        workflow.Versions.Should().HaveCount(1);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateArchiveRestoreDelete_ShouldAllThrow_WhenTheCallerDoesNotOwnTheWorkflow()
    {
        var workflow = CreatePublishedWorkflow();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        workflowRepository.GetByIdForOwnerAsync(workflow.Id, "someone-else", Arg.Any<CancellationToken>()).Returns((Workflow?)null);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns("someone-else");

        var duplicate = () => new DuplicateWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser).Handle(new DuplicateWorkflowCommand(workflow.Id), CancellationToken.None);
        var archive = () => new ArchiveWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser).Handle(new ArchiveWorkflowCommand(workflow.Id), CancellationToken.None);
        var restore = () => new RestoreWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser).Handle(new RestoreWorkflowCommand(workflow.Id), CancellationToken.None);
        var delete = () => new DeleteWorkflowCommandHandler(workflowRepository, unitOfWork, currentUser).Handle(new DeleteWorkflowCommand(workflow.Id), CancellationToken.None);

        await duplicate.Should().ThrowAsync<KeyNotFoundException>();
        await archive.Should().ThrowAsync<KeyNotFoundException>();
        await restore.Should().ThrowAsync<KeyNotFoundException>();
        await delete.Should().ThrowAsync<KeyNotFoundException>();
    }
}
