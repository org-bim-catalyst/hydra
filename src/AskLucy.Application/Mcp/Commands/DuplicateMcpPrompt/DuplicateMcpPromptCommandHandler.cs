using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Domain.Mcp;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DuplicateMcpPrompt;

public sealed class DuplicateMcpPromptCommandHandler(
    IMcpPromptRepository mcpPromptRepository,
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DuplicateMcpPromptCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(DuplicateMcpPromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var source = await mcpPromptRepository.GetByNamespacedNameAsync(request.NamespacedName, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP prompt '{request.NamespacedName}' was not found.");

        var newName = await ResolveNonConflictingNameAsync(userId, source.Name, cancellationToken);

        var content = new PromptContentSnapshot(
            SystemInstructions: null, DeveloperInstructions: null, UserInstructions: source.ContentTemplate,
            ContextText: null, ExamplesText: null, OutputInstructions: null, Constraints: null,
            ProviderKey: null, ModelKey: null, Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false);

        var (duplicate, version) = Prompt.Create(
            userId, newName, source.Description, PromptType.Chat, folderId: null, categoryId: null,
            PromptCapabilityRequirements.None, preferredModelKey: null, content, variables: [], userId);

        promptRepository.Add(duplicate);

        var usageStatistics = PromptUsageStatistics.CreateEmpty(duplicate.Id, userId);
        promptRepository.AddUsageStatistics(usageStatistics);

        auditLogRepository.Add(PromptAuditLog.Create(duplicate.Id, PromptAuditAction.Duplicated, userId, $"{{\"sourceMcpPromptNamespacedName\":\"{source.NamespacedName}\"}}"));

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
