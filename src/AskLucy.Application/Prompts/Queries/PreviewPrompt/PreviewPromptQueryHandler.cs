using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.PreviewPrompt;

public sealed class PreviewPromptQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<PreviewPromptQuery, PromptPreviewDto>
{
    public async Task<PromptPreviewDto> Handle(PreviewPromptQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        var currentVersion = await promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The prompt's current version could not be found.");

        var resolvedValues = PromptVariableResolver.ResolveForPreview(currentVersion.Variables, request.VariableValues);

        return new PromptPreviewDto(
            PromptVariableResolver.ResolveContent(currentVersion.SystemInstructions, resolvedValues),
            PromptVariableResolver.ResolveContent(currentVersion.DeveloperInstructions, resolvedValues),
            PromptVariableResolver.ResolveContent(currentVersion.UserInstructions, resolvedValues),
            PromptVariableResolver.ResolveContent(currentVersion.ContextText, resolvedValues),
            PromptVariableResolver.ResolveContent(currentVersion.OutputInstructions, resolvedValues),
            PromptVariableResolver.ResolveContent(currentVersion.Constraints, resolvedValues));
    }
}
