using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.ArchivePrompt;

public sealed class ArchivePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ArchivePromptCommand>
{
    public async Task Handle(ArchivePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        prompt.Archive(userId);
        auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Archived, userId, detailsJson: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
