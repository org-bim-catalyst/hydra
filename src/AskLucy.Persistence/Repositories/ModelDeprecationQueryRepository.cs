using AskLucy.Application.Ai.Queries.GetModelDeprecations;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>specs/069 contracts §3: reads only, counts only — no user identities leave this layer.</summary>
public sealed class ModelDeprecationQueryRepository(AskLucyDbContext dbContext) : IModelDeprecationQueryRepository
{
    public async Task<ModelDeprecationPageDto> ListAsync(
        Guid? providerId,
        bool awaitingValidation,
        bool openFlags,
        Guid? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ModelDeprecations
            .Join(dbContext.ModelDeprecationBatches, d => d.BatchId, b => b.Id, (d, b) => new { d, b })
            .Where(x => x.b.Kind == ModelDeprecationKind.Deprecation);

        if (providerId is { } id)
        {
            query = query.Where(x => x.b.ProviderId == id);
        }

        if (awaitingValidation)
        {
            query = query.Where(x => x.d.ValidationStatus == ReplacementValidationStatus.AwaitingValidation);
        }

        if (openFlags)
        {
            query = query.Where(x => dbContext.PlatformDefaultChanges.Any(c =>
                c.DeprecationId == x.d.Id && c.Outcome == DefaultChangeOutcome.Flagged && c.ResolvedAtUtc == null));
        }

        if (cursor is { } after)
        {
            // Newest-first cursor: strictly older than the cursor's own confirmation time.
            var cursorTime = await dbContext.ModelDeprecationBatches
                .Where(b => b.Id == after).Select(b => (DateTime?)b.ConfirmedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (cursorTime is { } at)
            {
                query = query.Where(x => x.b.ConfirmedAtUtc < at);
            }
        }

        var rows = await query.OrderByDescending(x => x.b.ConfirmedAtUtc).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        rows = rows.Take(pageSize).ToList();

        var modelIds = rows.SelectMany(r => new[] { r.d.ModelId })
            .Concat(rows.Where(r => r.d.ReplacementModelId != null).Select(r => r.d.ReplacementModelId!.Value))
            .ToHashSet();
        var models = await dbContext.AIModels.Where(m => modelIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ModelKey, m.DisplayName }).ToDictionaryAsync(m => m.Id, cancellationToken);
        var providers = await dbContext.AIProviders.Select(p => new { p.Id, p.DisplayName })
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        var changeRows = await dbContext.PlatformDefaultChanges
            .Where(c => rows.Select(r => r.d.Id).Contains(c.DeprecationId))
            .Select(c => new { c.DeprecationId, c.Outcome, c.ResolvedAtUtc })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var changes = changeRows.Where(c => c.DeprecationId == r.d.Id).ToList();
            return new ModelDeprecationSummaryDto(
                r.d.Id,
                r.d.ModelId,
                models.GetValueOrDefault(r.d.ModelId)?.ModelKey ?? string.Empty,
                models.GetValueOrDefault(r.d.ModelId)?.DisplayName ?? string.Empty,
                r.b.ProviderId,
                providers.GetValueOrDefault(r.b.ProviderId) ?? string.Empty,
                r.d.ReplacementModelId,
                r.d.ReplacementModelId is { } rid ? models.GetValueOrDefault(rid)?.DisplayName : null,
                r.d.ValidationStatus,
                changes.Count(c => c.Outcome == DefaultChangeOutcome.Reassigned),
                changes.Count(c => c.Outcome == DefaultChangeOutcome.Flagged && c.ResolvedAtUtc is null),
                r.b.UserImpactStatus,
                r.b.NotifiedUserCount,
                r.b.ConfirmedAtUtc,
                r.b.ConfirmedByUserId);
        }).ToList();

        return new ModelDeprecationPageDto(items, hasMore ? rows[^1].d.BatchId : null);
    }

    public async Task<ModelDeprecationDetailDto?> GetAsync(Guid deprecationId, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.ModelDeprecations.FirstOrDefaultAsync(d => d.Id == deprecationId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var batch = await dbContext.ModelDeprecationBatches.FirstOrDefaultAsync(b => b.Id == record.BatchId, cancellationToken);
        var model = await dbContext.AIModels.FirstOrDefaultAsync(m => m.Id == record.ModelId, cancellationToken);
        var replacement = record.ReplacementModelId is { } rid
            ? await dbContext.AIModels.FirstOrDefaultAsync(m => m.Id == rid, cancellationToken)
            : null;
        var providerName = await dbContext.AIProviders.Where(p => p.Id == batch!.ProviderId)
            .Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var changes = await dbContext.PlatformDefaultChanges
            .Where(c => c.DeprecationId == deprecationId)
            .ToListAsync(cancellationToken);
        var newModelIds = changes.Where(c => c.NewModelId != null).Select(c => c.NewModelId!.Value).ToHashSet();
        var newModels = await dbContext.AIModels.Where(m => newModelIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);

        return new ModelDeprecationDetailDto(
            record.Id,
            record.BatchId,
            record.ModelId,
            model?.ModelKey ?? string.Empty,
            model?.DisplayName ?? string.Empty,
            batch!.ProviderId,
            providerName,
            record.Reason,
            batch!.ConfirmedAtUtc,
            batch.ConfirmedByUserId,
            record.ReplacementModelId,
            replacement?.DisplayName,
            record.ReplacementSource,
            record.ValidationStatus,
            record.ValidatedByUserId,
            record.ValidatedAtUtc,
            batch.UserImpactStatus,
            batch.NotifiedUserCount,
            changes.Select(c => new PlatformDefaultChangeDto(
                c.TargetKind,
                c.Capability,
                c.Outcome,
                c.NewModelId,
                c.NewModelId is { } nid ? newModels.GetValueOrDefault(nid) : null,
                c.FlagReason,
                c.ServingProviderId,
                c.ServingModelId,
                c.IsOpenFlag,
                c.ResolvedByUserId,
                c.ResolvedAtUtc,
                c.ResolutionModelId)).ToList());
    }
}
