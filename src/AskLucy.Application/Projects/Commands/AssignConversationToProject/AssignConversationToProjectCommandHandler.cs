using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Projects.Authorization;
using MediatR;

namespace AskLucy.Application.Projects.Commands.AssignConversationToProject;

/// <summary>spec.md FR-002a — validates both the conversation and (when set) the target Project are owned by the caller before assigning.</summary>
public sealed class AssignConversationToProjectCommandHandler(
    IUserChatRepository chatRepository, IProjectRepository projectRepository,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser) : IRequestHandler<AssignConversationToProjectCommand>
{
    public async Task Handle(AssignConversationToProjectCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        if (request.ProjectId is { } projectId)
        {
            ProjectOwnershipGuard.EnsureOwnedBy(await projectRepository.GetByIdAsync(projectId, cancellationToken), userId);
        }

        chat.AssignToProject(request.ProjectId, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
