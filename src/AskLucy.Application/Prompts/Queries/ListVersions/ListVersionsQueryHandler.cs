using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListVersions;

public sealed class ListVersionsQueryHandler(
    IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListVersionsQuery, IReadOnlyList<PromptVersionSummaryDto>>
{
    public async Task<IReadOnlyList<PromptVersionSummaryDto>> Handle(ListVersionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var versions = await promptRepository.ListVersionsAsync(request.PromptId, cancellationToken);
        return [.. versions.Select(PromptVersionSummaryDto.FromEntity)];
    }
}
