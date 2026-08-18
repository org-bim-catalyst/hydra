using AskLucy.Application.Abstractions;
using AskLucy.Domain.Projects;
using MediatR;

namespace AskLucy.Application.Projects.Commands.CreateProject;

public sealed class CreateProjectCommandHandler(
    IProjectRepository projectRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var project = Project.Create(userId, request.Name, userId);
        projectRepository.Add(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProjectDto(project.Id, project.Name, project.CreatedAtUtc);
    }
}
