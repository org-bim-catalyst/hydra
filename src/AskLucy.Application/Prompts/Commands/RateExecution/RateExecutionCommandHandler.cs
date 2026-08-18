using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RateExecution;

public sealed class RateExecutionCommandHandler(
    IPromptExecutionRepository executionRepository,
    IPromptRepository promptRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RateExecutionCommand>
{
    public async Task Handle(RateExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var execution = await executionRepository.GetByIdAsync(request.ExecutionId, cancellationToken)
            ?? throw new KeyNotFoundException("Execution not found.");

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(execution.PromptId, userId, cancellationToken), userId);

        var existingRating = await executionRepository.GetRatingByExecutionIdAsync(execution.Id, cancellationToken);
        if (existingRating is not null)
        {
            existingRating.Update(request.RatingValue, userId);
        }
        else
        {
            executionRepository.AddRating(PromptRating.Create(execution.Id, request.RatingValue, userId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
