using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RestorePrompt;

public sealed class RestorePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RestorePromptCommand>
{
    public async Task Handle(RestorePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        prompt.Restore(userId);
        auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Restored, userId, detailsJson: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
