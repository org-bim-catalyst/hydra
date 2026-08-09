using AskLucy.Application.Abstractions;
using AskLucy.Application.Projects.Commands.DeleteProject;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Projects;
using FluentAssertions;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Projects;

/// <summary>tasks.md T080 (US5 AC3, research.md Decision 15) — deleting a Project archives, never immediately deletes, its scoped memories, which remain visible/exportable outside the Project context (archived, not soft-deleted, so they're excluded from ranking but not from the Memory Center/export).</summary>
public sealed class ProjectDeletionCascadeTests
{
    private readonly IProjectRepository _projectRepository = Substitute.For<IProjectRepository>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private const string UserId = "user-1";

    public ProjectDeletionCascadeTests() => _currentUser.UserId.Returns(UserId);

    [Fact]
    public async Task Handle_ShouldArchive_NeverSoftDelete_EveryMemoryScopedToTheDeletedProject()
    {
        var project = Project.Create(UserId, "Riverside Tower", UserId);
        _projectRepository.GetByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);

        var scopedMemories = Enumerable.Range(0, 2)
            .Select(i => MemoryEntity.CreateCandidate(
                UserId, project.Id, MemoryCategory.ProjectContext, $"Fact {i}", MemorySourceType.PassiveConversationAnalysis,
                null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test"))
            .ToList();
        _memoryRepository.GetByProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(scopedMemories);

        var handler = new DeleteProjectCommandHandler(_projectRepository, _memoryRepository, _unitOfWork, _currentUser);
        await handler.Handle(new DeleteProjectCommand(project.Id), CancellationToken.None);

        scopedMemories.Should().OnlyContain(m => m.State == MemoryLifecycleState.Archived);
        scopedMemories.Should().OnlyContain(m => !m.IsDeleted, "archived memories must remain visible/exportable, not soft-deleted");
        project.IsDeleted.Should().BeTrue();
    }
}
