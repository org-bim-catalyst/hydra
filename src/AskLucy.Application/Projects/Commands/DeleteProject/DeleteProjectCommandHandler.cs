using AskLucy.Application.Abstractions;
using AskLucy.Application.Projects.Authorization;
using MediatR;

namespace AskLucy.Application.Projects.Commands.DeleteProject;

/// <summary>
/// spec.md FR-002a, User Story 5 AC3, research.md Decision 15. Archives (never deletes) every
/// memory scoped to this Project as a direct cross-context repository call within the same
/// transaction, not a dispatched domain event — see <c>Project.cs</c>'s doc comment for why (no
/// domain-event dispatch infrastructure exists in this codebase); mirrors
/// <c>UpdateConversationKnowledgeBasesCommandHandler</c>'s established cross-context access
/// pattern.
/// </summary>
public sealed class DeleteProjectCommandHandler(
    IProjectRepository projectRepository, IMemoryRepository memoryRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser) : IRequestHandler<DeleteProjectCommand>
{
    public async Task Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var project = ProjectOwnershipGuard.EnsureOwnedBy(await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken), userId);

        var scopedMemories = await memoryRepository.GetByProjectAsync(project.Id, cancellationToken);
        foreach (var memory in scopedMemories)
        {
            memory.Archive(userId);
        }

        project.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
