using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.ApplyProviderModelSync;

/// <summary>
/// research.md Decision 2 — <see cref="AIModel.Create"/> always starts a model
/// `Available`; each added entry is immediately corrected to `Unavailable` via the
/// existing <see cref="AIModel.SetStatus"/> (FR-008's clarified default) rather than
/// widening `Create`'s signature for this one caller. No row is ever deleted (FR-008).
///
/// specs/009-selective-model-sync-review FR-007a/FR-007b: applies best-effort, per row —
/// a stale row (an `added.ModelKey` that already exists, or a `removedFromVendor.Id` that
/// doesn't belong to this provider) is skipped and reported in the result instead of
/// rejecting the whole request. Every row that isn't stale is still committed together in
/// exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> call (research.md Decision 2 —
/// preserves "one business transaction, one SaveChanges").
/// </summary>
public sealed class ApplyProviderModelSyncCommandHandler(
    IAIModelRepository models,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<ApplyProviderModelSyncCommandHandler> logger) : IRequestHandler<ApplyProviderModelSyncCommand, ApplyProviderModelSyncResultDto>
{
    public async Task<ApplyProviderModelSyncResultDto> Handle(ApplyProviderModelSyncCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var catalogModels = await models.ListByProviderIdAsync(request.ProviderId, cancellationToken);
        var catalogKeys = catalogModels.Select(m => m.ModelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogIds = catalogModels.Select(m => m.Id).ToHashSet();

        var appliedModelKeys = new List<string>();
        var failed = new List<SyncApplyFailureDto>();

        foreach (var added in request.Added)
        {
            if (catalogKeys.Contains(added.ModelKey))
            {
                failed.Add(new SyncApplyFailureDto(
                    added.ModelKey, added.DisplayName,
                    $"'{added.ModelKey}' already exists in the catalog — the diff is stale; re-run the sync check."));
                continue;
            }

            var model = AIModel.Create(
                request.ProviderId, added.ModelKey, added.DisplayName, added.ContextWindowTokens, added.MaxOutputTokens,
                added.Capabilities, releaseDate: null, pricing: null, actorUserId);
            model.SetStatus(AIModelStatus.Unavailable, actorUserId);
            models.Add(model);
            appliedModelKeys.Add(added.ModelKey);
        }

        foreach (var removed in request.RemovedFromVendor)
        {
            if (!catalogIds.Contains(removed.Id))
            {
                failed.Add(new SyncApplyFailureDto(
                    removed.ModelKey, removed.DisplayName,
                    $"Model '{removed.ModelKey}' does not belong to this provider — the diff is stale; re-run the sync check."));
                continue;
            }

            var model = await models.GetByIdAsync(removed.Id, cancellationToken)
                ?? throw new KeyNotFoundException("Model not found.");
            model.SetStatus(AIModelStatus.Unavailable, actorUserId);
            appliedModelKeys.Add(removed.ModelKey);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminAiModelSyncApplied(logger, actorUserId, request.ProviderId, request.Added.Count, request.RemovedFromVendor.Count);

        return new ApplyProviderModelSyncResultDto(appliedModelKeys, failed);
    }
}
