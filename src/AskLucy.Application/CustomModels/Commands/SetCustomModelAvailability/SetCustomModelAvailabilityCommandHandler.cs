using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels.Commands.SetCustomModelAvailability;

/// <summary>specs/072 FR-030, FR-034.</summary>
/// <remarks>
/// The one-Available-per-repository rule spans records, so it is checked here first to name the
/// model that holds it; two admins racing past the check are stopped by the filtered unique index,
/// which the repository reports as a <see cref="DuplicateResourceException"/> (409) too.
/// </remarks>
public sealed class SetCustomModelAvailabilityCommandHandler(
    ICustomModelRepository customModels,
    ICustomModelDeploymentNotifier notifier,
    CustomModelSummaryBuilder summaries,
    ICurrentUserAccessor currentUser,
    ILogger<SetCustomModelAvailabilityCommandHandler> logger) : IRequestHandler<SetCustomModelAvailabilityCommand, CustomModelSummaryDto>
{
    public async Task<CustomModelSummaryDto> Handle(SetCustomModelAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var model = await customModels.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("The custom model wasn't found.");

        if (model.Availability == request.Availability)
        {
            return await summaries.BuildAsync(model, cancellationToken);
        }

        if (request.Availability == CustomModelAvailability.Available)
        {
            if (model.DeploymentState != CustomModelDeploymentState.Completed)
            {
                throw new DomainRuleViolationException(CustomModelDtoMapping.NotCompletedReason);
            }

            var holder = await customModels.FindAvailableForRepositoryAsync(model.RepositoryId, cancellationToken);
            if (holder is not null && holder.Id != model.Id)
            {
                throw new DuplicateResourceException(
                    $"\"{holder.Name}\" is already available for {model.RepositoryId}. Make it unavailable first.");
            }
        }

        var previous = model.Availability;
        var saved = await customModels.UpdateAsync(
            request.Id,
            m =>
            {
                // Safe to run twice: the repository re-applies this after a RowVersion conflict.
                previous = m.Availability;
                if (request.Availability == CustomModelAvailability.Available)
                {
                    m.MakeAvailable();
                }
                else
                {
                    m.MakeUnavailable();
                }

                return true;
            },
            cancellationToken) ?? throw new KeyNotFoundException("The custom model wasn't found.");

        CustomModelAdminActionLog.AvailabilityChanged(logger, actorUserId, saved.Id, saved.Name, previous, saved.Availability);

        var summary = await summaries.BuildAsync(saved, cancellationToken);
        await notifier.NotifyStateChangedAsync(summary, cancellationToken: cancellationToken);
        return summary;
    }
}
