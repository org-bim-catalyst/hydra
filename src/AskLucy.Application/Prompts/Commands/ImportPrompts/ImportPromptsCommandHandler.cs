using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.ImportPrompts;

/// <summary>
/// Validates the entire file atomically before creating anything (FR-071, research.md Decision 13
/// — if any entry fails, nothing is persisted), then creates each entry as an independent prompt
/// with its own fresh version-1 history (FR-072), auto-suffixing on a name collision the same way
/// <c>DuplicatePromptCommandHandler</c> does (FR-006).
/// </summary>
public sealed class ImportPromptsCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ImportPromptsCommand, IReadOnlyList<PromptListItemDto>>
{
    public async Task<IReadOnlyList<PromptListItemDto>> Handle(ImportPromptsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var validation = PromptImportValidator.Validate(request.File);
        if (!validation.IsValid)
        {
            throw new ValidationException(
                validation.Errors.Select(e => new ValidationFailure($"prompts[{e.EntryIndex}]", e.Message)));
        }

        var created = new List<PromptListItemDto>();
        foreach (var entry in request.File.Prompts)
        {
            var name = await ResolveNonConflictingNameAsync(userId, entry.Name.Trim(), cancellationToken);

            var content = new PromptContentSnapshot(
                entry.SystemInstructions, entry.DeveloperInstructions, entry.UserInstructions,
                entry.ContextText, entry.ExamplesText, entry.OutputInstructions, entry.Constraints,
                ProviderKey: null, ModelKey: null, Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false);

            var variableDefinitions = entry.Variables.Select(v => v.ToDefinition()).ToList();

            var (prompt, _) = Prompt.Create(
                userId, name, entry.Description, entry.PromptType, folderId: null, categoryId: null,
                entry.RequiredCapabilities, entry.PreferredModelKey, content, variableDefinitions, userId);

            foreach (var tag in entry.Tags)
            {
                prompt.AddTag(tag, userId, userId);
            }

            promptRepository.Add(prompt);

            var usageStatistics = PromptUsageStatistics.CreateEmpty(prompt.Id, userId);
            promptRepository.AddUsageStatistics(usageStatistics);

            auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Imported, userId, detailsJson: null));

            created.Add(PromptListItemDto.FromEntity(prompt, 0, null));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return created;
    }

    private async Task<string> ResolveNonConflictingNameAsync(string ownerId, string desiredName, CancellationToken cancellationToken)
    {
        var candidate = desiredName;
        var suffix = 2;

        while (await promptRepository.GetByOwnerAndNameAsync(ownerId, candidate, cancellationToken) is not null)
        {
            candidate = $"{desiredName} {suffix}";
            suffix++;
        }

        return candidate;
    }
}
