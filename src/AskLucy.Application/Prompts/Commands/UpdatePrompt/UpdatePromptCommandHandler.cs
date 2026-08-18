using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.UpdatePrompt;

public sealed class UpdatePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdatePromptCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(UpdatePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        if (!string.Equals(prompt.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var existing = await promptRepository.GetByOwnerAndNameAsync(userId, request.Name.Trim(), cancellationToken);
            if (existing is not null && existing.Id != prompt.Id)
            {
                throw new DuplicateResourceException($"You already have a prompt named '{request.Name.Trim()}'.");
            }

            prompt.Rename(request.Name, userId);
        }

        if (prompt.FolderId != request.FolderId)
        {
            prompt.SetFolder(request.FolderId, userId);
        }

        if (prompt.CategoryId != request.CategoryId)
        {
            prompt.SetCategory(request.CategoryId, userId);
        }

        var contentFields = new[]
        {
            request.SystemInstructions, request.DeveloperInstructions, request.UserInstructions,
            request.ContextText, request.ExamplesText, request.OutputInstructions, request.Constraints,
        };
        var declaredNames = request.Variables.Select(v => v.Name).ToList();
        var analysis = PromptContentAnalyzer.Analyze(contentFields, declaredNames);
        if (!analysis.IsValid)
        {
            throw new DomainRuleViolationException(BuildAnalysisErrorMessage(analysis));
        }

        var content = new PromptContentSnapshot(
            request.SystemInstructions, request.DeveloperInstructions, request.UserInstructions,
            request.ContextText, request.ExamplesText, request.OutputInstructions, request.Constraints,
            ProviderKey: null, ModelKey: null, Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false);

        var variableDefinitions = request.Variables.Select(v => v.ToDefinition()).ToList();

        var version = prompt.ApplyEdit(content, variableDefinitions, request.ChangeDescription, userId);

        auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Updated, userId, detailsJson: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var usageStatistics = await promptRepository.GetUsageStatisticsAsync(prompt.Id, cancellationToken);
        return PromptDetailDto.Create(prompt, version, usageStatistics);
    }

    private static string BuildAnalysisErrorMessage(PromptContentAnalysisResult analysis)
    {
        var parts = new List<string>();
        if (analysis.UndeclaredPlaceholders.Count > 0)
        {
            parts.Add($"undeclared placeholder(s): {string.Join(", ", analysis.UndeclaredPlaceholders)}");
        }

        if (analysis.UnreferencedVariables.Count > 0)
        {
            parts.Add($"unreferenced variable(s): {string.Join(", ", analysis.UnreferencedVariables)}");
        }

        return $"Prompt content and variables are inconsistent: {string.Join("; ", parts)}.";
    }
}
