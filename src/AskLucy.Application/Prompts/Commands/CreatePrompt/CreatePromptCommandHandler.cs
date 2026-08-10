using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.CreatePrompt;

public sealed class CreatePromptCommandHandler(
    IPromptRepository promptRepository,
    IPromptAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreatePromptCommand, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(CreatePromptCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // Name uniqueness per owner, case-insensitive (FR-006, research.md Decision 7).
        if (await promptRepository.GetByOwnerAndNameAsync(userId, request.Name.Trim(), cancellationToken) is not null)
        {
            throw new DuplicateResourceException($"You already have a prompt named '{request.Name.Trim()}'.");
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

        var (prompt, version) = Prompt.Create(
            userId, request.Name, request.Description, request.PromptType, request.FolderId, request.CategoryId,
            request.RequiredCapabilities, request.PreferredModelKey, content, variableDefinitions, userId);

        promptRepository.Add(prompt);

        var usageStatistics = PromptUsageStatistics.CreateEmpty(prompt.Id, userId);
        promptRepository.AddUsageStatistics(usageStatistics);

        auditLogRepository.Add(PromptAuditLog.Create(prompt.Id, PromptAuditAction.Created, userId, detailsJson: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);

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
