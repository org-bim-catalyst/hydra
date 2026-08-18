using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetPromptStatistics;

public sealed class GetPromptStatisticsQueryHandler(
    IPromptRepository promptRepository,
    IPromptExecutionRepository executionRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPromptStatisticsQuery, PromptStatisticsDto>
{
    public async Task<PromptStatisticsDto> Handle(GetPromptStatisticsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var usageStatistics = await promptRepository.GetUsageStatisticsAsync(prompt.Id, cancellationToken);
        var ratingBreakdown = await executionRepository.GetRatingBreakdownByPromptIdAsync(prompt.Id, cancellationToken);

        return new PromptStatisticsDto(
            usageStatistics?.SuccessfulExecutionCount ?? 0, usageStatistics?.LastSuccessfulUseAtUtc, ratingBreakdown);
    }
}
