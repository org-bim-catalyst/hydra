using AskLucy.Application.Abstractions;
using AskLucy.Application.Projects.Authorization;
using MediatR;

namespace AskLucy.Application.Projects.Commands.RenameProject;

public sealed class RenameProjectCommandHandler(
    IProjectRepository projectRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RenameProjectCommand>
{
    public async Task Handle(RenameProjectCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var project = ProjectOwnershipGuard.EnsureOwnedBy(await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken), userId);

        project.Rename(request.Name, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
