using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.DuplicateVersion;

public sealed class DuplicateVersionCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicateVersionCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(DuplicateVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var source = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var sourceVersion = await promptRepository.GetVersionAsync(request.PromptId, request.VersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var newName = await ResolveNonConflictingNameAsync(userId, $"{source.Name} (v{request.VersionNumber} copy)", cancellationToken);

        var content = new PromptContentSnapshot(
            sourceVersion.SystemInstructions, sourceVersion.DeveloperInstructions, sourceVersion.UserInstructions,
            sourceVersion.ContextText, sourceVersion.ExamplesText, sourceVersion.OutputInstructions, sourceVersion.Constraints,
            ProviderKey: null, ModelKey: null, Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false);

        var variableDefinitions = sourceVersion.Variables
            .OrderBy(v => v.OrderIndex)
            .Select(v => new PromptVariableDefinition(
                v.Name, v.Description, v.VariableType, v.IsRequired, v.DefaultValue, v.ExampleValue, v.ValidationRulesJson, v.OrderIndex))
            .ToList();

        var (duplicate, newVersion) = Prompt.Create(
            userId, newName, source.Description, source.PromptType, source.FolderId, source.CategoryId,
            source.RequiredCapabilities, source.PreferredModelKey, content, variableDefinitions, userId);

        promptRepository.Add(duplicate);

        var usageStatistics = PromptUsageStatistics.CreateEmpty(duplicate.Id, userId);
        promptRepository.AddUsageStatistics(usageStatistics);

        auditLogRepository.Add(PromptAuditLog.Create(
            duplicate.Id, PromptAuditAction.Duplicated, userId,
            $"{{\"sourcePromptId\":\"{source.Id}\",\"sourceVersionNumber\":{request.VersionNumber}}}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptDetailDto.Create(duplicate, newVersion, usageStatistics);
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
