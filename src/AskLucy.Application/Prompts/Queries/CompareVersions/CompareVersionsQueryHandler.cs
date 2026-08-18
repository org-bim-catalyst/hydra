using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.CompareVersions;

public sealed class CompareVersionsQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<CompareVersionsQuery, PromptVersionComparisonDto>
{
    public async Task<PromptVersionComparisonDto> Handle(CompareVersionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var from = await promptRepository.GetVersionAsync(request.PromptId, request.FromVersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");
        var to = await promptRepository.GetVersionAsync(request.PromptId, request.ToVersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var differences = new List<PromptVersionFieldDiff>();
        void Compare(string field, string? fromValue, string? toValue)
        {
            if (fromValue != toValue)
            {
                differences.Add(new PromptVersionFieldDiff(field, fromValue, toValue));
            }
        }

        Compare(nameof(PromptVersion.SystemInstructions), from.SystemInstructions, to.SystemInstructions);
        Compare(nameof(PromptVersion.DeveloperInstructions), from.DeveloperInstructions, to.DeveloperInstructions);
        Compare(nameof(PromptVersion.UserInstructions), from.UserInstructions, to.UserInstructions);
        Compare(nameof(PromptVersion.ContextText), from.ContextText, to.ContextText);
        Compare(nameof(PromptVersion.ExamplesText), from.ExamplesText, to.ExamplesText);
        Compare(nameof(PromptVersion.OutputInstructions), from.OutputInstructions, to.OutputInstructions);
        Compare(nameof(PromptVersion.Constraints), from.Constraints, to.Constraints);
        Compare(nameof(PromptVersion.ProviderKey), from.ProviderKey, to.ProviderKey);
        Compare(nameof(PromptVersion.ModelKey), from.ModelKey, to.ModelKey);
        Compare(nameof(PromptVersion.Temperature), from.Temperature?.ToString(), to.Temperature?.ToString());
        Compare(nameof(PromptVersion.MaxOutputTokens), from.MaxOutputTokens?.ToString(), to.MaxOutputTokens?.ToString());

        var fromVariableNames = from.Variables.Select(v => v.Name).OrderBy(n => n).ToList();
        var toVariableNames = to.Variables.Select(v => v.Name).OrderBy(n => n).ToList();
        Compare("Variables", string.Join(", ", fromVariableNames), string.Join(", ", toVariableNames));

        return new PromptVersionComparisonDto(
            PromptVersionSummaryDto.FromEntity(from), PromptVersionSummaryDto.FromEntity(to), differences);
    }
}
