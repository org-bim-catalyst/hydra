using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.ExportPrompts;

public sealed class ExportPromptsCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ExportPromptsCommand, PromptExportFile>
{
    public async Task<PromptExportFile> Handle(ExportPromptsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var entries = new List<(Prompt Prompt, PromptVersion CurrentVersion)>();
        foreach (var promptId in request.PromptIds)
        {
            var prompt = PromptOwnershipGuard.EnsureOwnedBy(
                await promptRepository.GetByIdForOwnerAsync(promptId, userId, cancellationToken), userId);
            var currentVersion = await promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, cancellationToken)
                ?? throw new InvalidOperationException("The prompt's current version could not be found.");

            entries.Add((prompt, currentVersion));
            auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Exported, userId, detailsJson: null));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptExportFileBuilder.Build(entries);
    }
}
