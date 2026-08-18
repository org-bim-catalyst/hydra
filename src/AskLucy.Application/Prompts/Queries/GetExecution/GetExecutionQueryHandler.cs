using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetExecution;

public sealed class GetExecutionQueryHandler(
    IPromptExecutionRepository executionRepository,
    IPromptRepository promptRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetExecutionQuery, PromptExecutionDetailDto>
{
    public async Task<PromptExecutionDetailDto> Handle(GetExecutionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var execution = await executionRepository.GetByIdAsync(request.ExecutionId, cancellationToken)
            ?? throw new KeyNotFoundException("Execution not found.");

        // Ownership is scoped through the owning Prompt (FR-090) — an execution has no owner
        // field of its own.
        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(execution.PromptId, userId, cancellationToken), userId);

        var version = await promptRepository.GetVersionByIdAsync(execution.PromptVersionId, cancellationToken);
        var result = await executionRepository.GetResultByExecutionIdAsync(execution.Id, cancellationToken);
        var rating = await executionRepository.GetRatingByExecutionIdAsync(execution.Id, cancellationToken);

        return PromptExecutionDetailDto.FromEntities(execution, version?.VersionNumber ?? 0, result, rating);
    }
}
