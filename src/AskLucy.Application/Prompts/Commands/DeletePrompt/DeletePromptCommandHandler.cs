using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeletePrompt;

public sealed class DeletePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeletePromptCommand>
{
    public async Task Handle(DeletePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        prompt.SoftDelete(userId);
        auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Deleted, userId, detailsJson: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
