using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListPrompts;

public sealed class ListPromptsQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListPromptsQuery, PagedResult<PromptListItemDto>>
{
    public async Task<PagedResult<PromptListItemDto>> Handle(ListPromptsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (prompts, nextCursor) = await promptRepository.SearchAsync(
            userId, request.View, request.Query, request.CategoryId, request.Tag, request.FolderId, request.Status,
            request.Cursor, request.PageSize, cancellationToken);

        var usageStatisticsByPromptId = await promptRepository.GetUsageStatisticsByPromptIdsAsync(
            [.. prompts.Select(p => p.Id)], cancellationToken);

        var items = prompts
            .Select(prompt =>
            {
                usageStatisticsByPromptId.TryGetValue(prompt.Id, out var usageStatistics);
                return PromptListItemDto.FromEntity(prompt, usageStatistics?.SuccessfulExecutionCount ?? 0, usageStatistics?.LastSuccessfulUseAtUtc);
            })
            .ToList();

        return new PagedResult<PromptListItemDto>(items, nextCursor);
    }
}
