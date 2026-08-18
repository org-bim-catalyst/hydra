using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetPrompt;

public sealed class GetPromptQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetPromptQuery, PromptDetailDto>
{
    public async Task<PromptDetailDto> Handle(GetPromptQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        var currentVersion = await promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The prompt's current version could not be found.");

        var usageStatistics = await promptRepository.GetUsageStatisticsAsync(prompt.Id, cancellationToken);

        return PromptDetailDto.Create(prompt, currentVersion, usageStatistics);
    }
}
