using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RestoreVersion;

public sealed class RestoreVersionCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RestoreVersionCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(RestoreVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var versionToRestore = await promptRepository.GetVersionAsync(request.PromptId, request.VersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var newVersion = prompt.RestoreFrom(versionToRestore, userId);

        auditLogRepository.Add(PromptAuditLog.Create(
            prompt.Id, PromptAuditAction.VersionRestored, userId,
            $"{{\"restoredFromVersion\":{request.VersionNumber}}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var usageStatistics = await promptRepository.GetUsageStatisticsAsync(prompt.Id, cancellationToken);
        return PromptDetailDto.Create(prompt, newVersion, usageStatistics);
    }
}
