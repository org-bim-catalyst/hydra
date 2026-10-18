using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Ai.Commands.ValidateModelReplacement;

/// <summary>
/// specs/069 FR-011b: an administrator replaces the replacement during validation. Platform
/// defaults still on the old replacement move at once; the owners' items move in a background
/// job so the request stays fast. Items the owner has changed since are left alone.
/// </summary>
public sealed record ChangeModelReplacementCommand(Guid DeprecationId, Guid ModelId) : IRequest<Guid>;

public sealed class ChangeModelReplacementCommandHandler(
    IModelDeprecationRepository deprecations,
    IAIModelRepository models,
    IAIProviderRepository providers,
    IAiCapabilityAssignmentRepository assignments,
    ICurrentUserAccessor currentUser,
    IUnitOfWork unitOfWork,
    IBackgroundJobClient backgroundJobClient) : IRequestHandler<ChangeModelReplacementCommand, Guid>
{
    public async Task<Guid> Handle(ChangeModelReplacementCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var deprecation = await deprecations.GetByIdAsync(request.DeprecationId, cancellationToken)
            ?? throw new KeyNotFoundException("Deprecation not found.");
        var retired = await models.GetByIdAsync(deprecation.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");
        var replacement = await models.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Replacement model not found.");

        if (replacement.Status != AIModelStatus.Available || replacement.ProviderId != retired.ProviderId)
        {
            throw new ModelReplacementRejectedException("The chosen model must be Available and belong to the same provider.");
        }

        var previousReplacementId = deprecation.ReplacementModelId;
        var now = DateTime.UtcNow;
        try
        {
            deprecation.ChangeReplacement(replacement.Id, actor, now);
        }
        catch (DomainRuleViolationException ex)
        {
            throw new ModelReplacementConflictException(ex.Message);
        }

        if (previousReplacementId is { } oldId)
        {
            await MoveDefaultsAsync(retired, oldId, replacement, actor, now, cancellationToken);
        }

        var batch = ModelDeprecationBatch.ReplacementChange(
            retired.ProviderId, deprecation.Id, previousReplacementId ?? Guid.Empty, actor, now);
        deprecations.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<IModelDeprecationReplacementChangeJob>(j => j.RunAsync(batch.Id, CancellationToken.None));
        return batch.Id;
    }

    private async Task MoveDefaultsAsync(
        AIModel retired, Guid oldReplacementId, AIModel replacement, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(retired.ProviderId, cancellationToken);
        if (provider?.DefaultModelId == oldReplacementId && PlatformFunctionRequirements.SatisfiesProviderDefault(replacement))
        {
            provider.SetDefaultModel(replacement.Id, actor);
        }

        foreach (var assignment in (await assignments.ListAllAsync(cancellationToken))
                     .Where(a => a.ModelId == oldReplacementId && PlatformFunctionRequirements.Satisfies(a.Capability, replacement)))
        {
            assignment.AssignTo(retired.ProviderId, replacement.Id, actor);
        }
    }
}

/// <summary>The chosen model cannot serve as the replacement. Mapped to 400.</summary>
public sealed class ModelReplacementRejectedException(string message) : Exception(message);
