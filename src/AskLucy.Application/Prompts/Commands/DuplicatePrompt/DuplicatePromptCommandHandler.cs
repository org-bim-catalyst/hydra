using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.DuplicatePrompt;

public sealed class DuplicatePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicatePromptCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(DuplicatePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var source = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        var currentVersion = await promptRepository.GetVersionAsync(source.Id, source.CurrentVersionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The prompt's current version could not be found.");

        var newName = await ResolveNonConflictingNameAsync(userId, $"{source.Name} (copy)", cancellationToken);

        var content = new PromptContentSnapshot(
            currentVersion.SystemInstructions, currentVersion.DeveloperInstructions, currentVersion.UserInstructions,
            currentVersion.ContextText, currentVersion.ExamplesText, currentVersion.OutputInstructions, currentVersion.Constraints,
            ProviderKey: null, ModelKey: null, Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false);

        var variableDefinitions = currentVersion.Variables
            .OrderBy(v => v.OrderIndex)
            .Select(v => new PromptVariableDefinition(
                v.Name, v.Description, v.VariableType, v.IsRequired, v.DefaultValue, v.ExampleValue, v.ValidationRulesJson, v.OrderIndex))
            .ToList();

        var (duplicate, version) = Prompt.Create(
            userId, newName, source.Description, source.PromptType, source.FolderId, source.CategoryId,
            source.RequiredCapabilities, source.PreferredModelKey, content, variableDefinitions, userId);

        promptRepository.Add(duplicate);

        var usageStatistics = PromptUsageStatistics.CreateEmpty(duplicate.Id, userId);
        promptRepository.AddUsageStatistics(usageStatistics);

        auditLogRepository.Add(PromptAuditLog.Create(duplicate.Id, PromptAuditAction.Duplicated, userId, $"{{\"sourcePromptId\":\"{source.Id}\"}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptDetailDto.Create(duplicate, version, usageStatistics);
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
