using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.CompareExecutions;

public sealed class CompareExecutionsQueryHandler(
    IPromptExecutionRepository executionRepository,
    IPromptRepository promptRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<CompareExecutionsQuery, IReadOnlyList<PromptExecutionDetailDto>>
{
    public async Task<IReadOnlyList<PromptExecutionDetailDto>> Handle(CompareExecutionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var executions = await executionRepository.ListByIdsAsync(request.ExecutionIds, cancellationToken);

        var results = new List<PromptExecutionDetailDto>(executions.Count);
        foreach (var execution in executions)
        {
            // Silently skip an execution the caller doesn't own (FR-090) rather than failing the
            // whole comparison — matches how a mixed-ownership id list is handled elsewhere
            // (e.g. IKnowledgeBaseRepository.ResolveOwnedIdsAsync's "silently excluded" contract).
            var owningPrompt = await promptRepository.GetByIdForOwnerAsync(execution.PromptId, userId, cancellationToken);
            if (owningPrompt is null)
            {
                continue;
            }

            var version = await promptRepository.GetVersionByIdAsync(execution.PromptVersionId, cancellationToken);
            var result = await executionRepository.GetResultByExecutionIdAsync(execution.Id, cancellationToken);
            var rating = await executionRepository.GetRatingByExecutionIdAsync(execution.Id, cancellationToken);

            results.Add(PromptExecutionDetailDto.FromEntities(execution, version?.VersionNumber ?? 0, result, rating));
        }

        return results;
    }
}
