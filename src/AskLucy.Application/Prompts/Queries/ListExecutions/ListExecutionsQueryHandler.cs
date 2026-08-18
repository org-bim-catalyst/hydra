using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListExecutions;

public sealed class ListExecutionsQueryHandler(
    IPromptExecutionRepository executionRepository,
    IPromptRepository promptRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListExecutionsQuery, PagedResult<PromptExecutionSummaryDto>>
{
    public async Task<PagedResult<PromptExecutionSummaryDto>> Handle(ListExecutionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var (executions, nextCursor) = await executionRepository.ListForPromptAsync(
            request.PromptId, request.Cursor, request.PageSize, cancellationToken);

        var items = new List<PromptExecutionSummaryDto>(executions.Count);
        foreach (var execution in executions)
        {
            var version = await promptRepository.GetVersionByIdAsync(execution.PromptVersionId, cancellationToken);
            var result = await executionRepository.GetResultByExecutionIdAsync(execution.Id, cancellationToken);
            items.Add(PromptExecutionSummaryDto.FromEntity(execution, version?.VersionNumber ?? 0, result?.EstimatedCostUsd));
        }

        return new PagedResult<PromptExecutionSummaryDto>(items, nextCursor);
    }
}
