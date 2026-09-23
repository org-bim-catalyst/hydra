using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Jobs;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using FluentValidation;
using FluentValidation.Results;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;

/// <summary>specs/072 plan.md "Key flows" step 1.</summary>
public sealed class SubmitCustomModelDeploymentCommandHandler(
    ICustomModelRepository customModels,
    IDeploymentTargetSettingsProvider deploymentTarget,
    IBackgroundJobClient backgroundJobs,
    ICustomModelDeploymentNotifier notifier,
    CustomModelSummaryBuilder summaries,
    IOptionsMonitor<CustomModelsOptions> options,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<SubmitCustomModelDeploymentCommandHandler> logger) : IRequestHandler<SubmitCustomModelDeploymentCommand, SubmittedCustomModelDto>
{
    public async Task<SubmittedCustomModelDto> Handle(SubmitCustomModelDeploymentCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // Re-checked here, not only on the status endpoint, so a config edit racing the dialog
        // still gets the clear "not configured" answer rather than a job that fails later.
        if (await deploymentTarget.GetAsync(cancellationToken) is null)
        {
            throw new CustomModelDeploymentNotConfiguredException();
        }

        if (!HuggingFaceModelSource.TryParse(request.Source, out var source, out var sourceError))
        {
            throw Invalid("source", sourceError);
        }

        if (!DeploymentDestination.TryCreate(request.Destination, options.CurrentValue.GetAllowedDestinationPrefixes(), out var destination, out var destinationError))
        {
            throw Invalid("destination", destinationError);
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? source.DerivedName : request.Name.Trim();
        if (name is null)
        {
            throw Invalid("name", "A name couldn't be derived from the source. Enter a name.");
        }

        if (await customModels.NameExistsAsync(name, cancellationToken))
        {
            throw new DuplicateResourceException($"A custom model named '{name}' already exists. Enter a different name.");
        }

        var overlapping = await customModels.FindActiveJobOverlappingDestinationAsync(destination.Value, cancellationToken);
        if (overlapping is not null)
        {
            throw new DuplicateResourceException(
                $"'{overlapping.Name}' is still deploying to '{overlapping.Destination}', which overlaps '{destination.Value}'. Wait for it to finish or cancel it first.");
        }

        var model = CustomModel.Create(name, source, destination, actorUserId);
        await customModels.AddAsync(model, cancellationToken);

        string backgroundJobId;
        try
        {
            backgroundJobId = backgroundJobs.Enqueue<ICustomModelDeploymentJob>(job => job.RunAsync(model.Id, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // The record exists but nothing will ever run it: fail it now rather than leave a
            // Queued row that blocks its destination until the next startup sweep.
            CustomModelAdminActionLog.Failed(logger, ex, model.Id, model.Name, CustomModelDeploymentState.Failed, CustomModelFailureKind.Unexpected, "The deployment job couldn't be queued.");
            await customModels.UpdateAsync(
                model.Id,
                m =>
                {
                    if (!m.IsInProgress)
                    {
                        return false;
                    }

                    m.Fail(CustomModelFailureKind.Unexpected, "The deployment couldn't be queued. Try again.", timeProvider.GetUtcNow().UtcDateTime);
                    return true;
                },
                CancellationToken.None);
            throw;
        }

        // Declined when the job already left Queued, which only matters for cancelling a queued job.
        var saved = await customModels.UpdateAsync(
            model.Id,
            m =>
            {
                if (m.DeploymentState != CustomModelDeploymentState.Queued)
                {
                    return false;
                }

                m.AssignBackgroundJob(backgroundJobId);
                return true;
            },
            cancellationToken) ?? model;

        CustomModelAdminActionLog.Submitted(logger, actorUserId, saved.Id, saved.Name, saved.RepositoryId, saved.Revision, saved.Destination);

        var summary = await summaries.BuildAsync(saved, cancellationToken);
        await notifier.NotifyStateChangedAsync(summary, cancellationToken: cancellationToken);

        return new SubmittedCustomModelDto(summary, source.IgnoredFilePath);
    }

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
