using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/074 research D3. Opening an incident goes through the tracked aggregate, so the audit
/// interceptor stamps it (<c>CreatedBy = "system"</c> from the writer's scope). Every later change
/// on the append path — counters, the stored-occurrence slot, distinct counts, recoveries — is a
/// set-based bookkeeping write that deliberately leaves <c>ModifiedAtUtc</c>/<c>ModifiedBy</c>
/// untouched, like the repo's other <c>ExecuteUpdateAsync</c> uses; <c>LastSeenUtc</c> is the
/// activity timestamp (data-model → Audit columns).
/// </summary>
public sealed class OperationalFailureStore(AskLucyDbContext dbContext, IOptions<OperationalFailuresOptions> options)
    : IOperationalFailureStore
{
    private const int TransitionBatchSize = 100;
    private const int MaxTransitionAttempts = 3;

    private readonly int _maxStoredOccurrences = OperationalFailuresOptions.Normalize(options.Value).MaxStoredOccurrencesPerIncident;

    public async Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = Clip(request);

        var incidentId = await FindUnresolvedAsync(request.GroupingKey, cancellationToken);
        var opened = false;
        if (incidentId is null)
        {
            (incidentId, opened) = await OpenAsync(request, cancellationToken);
        }

        var severity = await JoinAsync(incidentId.Value, request, cancellationToken);
        return new IncidentAppendResult(incidentId.Value, opened, severity);
    }

    public async Task<bool> IncrementRecoveryAsync(string groupingKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingKey);

        var updated = await dbContext.OperationalFailureIncidents
            .Where(i => i.GroupingKey == groupingKey && i.TriageState != IncidentTriageState.Resolved)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RecoveryCount, i => i.RecoveryCount + 1), cancellationToken);

        return updated > 0;
    }

    public async Task<(IReadOnlyList<OperationalFailureIncident> Items, int TotalCount)> ListIncidentsAsync(
        IncidentFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = Visible().Where(i => i.LastSeenUtc >= filter.FromUtc && i.LastSeenUtc <= filter.ToUtc);

        query = filter.State switch
        {
            IncidentStateFilter.Open => query.Where(i => i.TriageState == IncidentTriageState.Open),
            IncidentStateFilter.Acknowledged => query.Where(i => i.TriageState == IncidentTriageState.Acknowledged),
            IncidentStateFilter.Resolved => query.Where(i => i.TriageState == IncidentTriageState.Resolved),
            _ => query.Where(i => i.TriageState != IncidentTriageState.Resolved),
        };

        if (filter.Severities is { Count: > 0 } severities)
        {
            query = query.Where(i => severities.Contains(i.HighestSeverity));
        }

        if (filter.Engines is { Count: > 0 } engines)
        {
            query = query.Where(i => engines.Contains(i.Engine));
        }

        if (!string.IsNullOrWhiteSpace(filter.Provider))
        {
            query = query.Where(i => i.ProviderName == filter.Provider);
        }

        if (filter.Kinds is { Count: > 0 } kinds)
        {
            query = query.Where(i => kinds.Contains(i.Kind));
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            var userId = filter.UserId;
            query = query.Where(i => dbContext.OperationalFailureIncidentParticipants.Any(p =>
                p.IncidentId == i.Id && p.ParticipantType == IncidentParticipantType.User && p.ParticipantKey == userId));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.LastSeenUtc)
            .ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<OperationalFailureIncident?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
        Visible().FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

    public async Task<(IReadOnlyList<OperationalFailureOccurrence> Items, int TotalCount)> ListOccurrencesAsync(
        Guid incidentId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.OperationalFailureOccurrences.AsNoTracking().Where(o => o.IncidentId == incidentId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.OccurredAtUtc)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<OperationalFailureOccurrence>?> FindItemReferencesAsync(
        Guid incidentId, InvestigatedItemType itemType, Guid itemId, CancellationToken cancellationToken = default)
    {
        var incident = await Visible()
            .Where(i => i.Id == incidentId)
            .Select(i => new { i.SubjectType, i.SubjectId })
            .FirstOrDefaultAsync(cancellationToken);
        if (incident is null)
        {
            return null;
        }

        var occurrences = dbContext.OperationalFailureOccurrences.AsNoTracking().Where(o => o.IncidentId == incidentId);
        occurrences = itemType switch
        {
            InvestigatedItemType.Chat => occurrences.Where(o => o.ChatId == itemId),
            InvestigatedItemType.WorkflowRun => occurrences.Where(o => o.WorkflowExecutionId == itemId),
            InvestigatedItemType.Document => occurrences.Where(o => o.DocumentId == itemId),
            _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null),
        };

        var referencing = await occurrences.OrderBy(o => o.OccurredAtUtc).ToListAsync(cancellationToken);

        // Only a document can be an incident's subject; a run's subject is its workflow, not the run.
        var isSubject = itemType == InvestigatedItemType.Document
            && incident.SubjectId == itemId
            && incident.SubjectType == nameof(ReferencedItemKind.Document);

        return referencing.Count > 0 || isSubject ? referencing : null;
    }

    public async Task<IReadOnlyDictionary<string, int>> CountUnresolvedByRootCauseAsync(
        IReadOnlyCollection<string> rootCauseKeys, CancellationToken cancellationToken = default)
    {
        if (rootCauseKeys.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return await Visible()
            .Where(i => rootCauseKeys.Contains(i.RootCauseKey) && i.TriageState != IncidentTriageState.Resolved)
            .GroupBy(i => i.RootCauseKey)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListRecentUserIdsAsync(Guid incidentId, int take, CancellationToken cancellationToken = default) =>
        await dbContext.OperationalFailureIncidentParticipants
            .AsNoTracking()
            .Where(p => p.IncidentId == incidentId && p.ParticipantType == IncidentParticipantType.User)
            .OrderByDescending(p => p.FirstSeenUtc)
            .Select(p => p.ParticipantKey)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<IncidentTransitionOutcome>> TransitionAsync(
        IReadOnlyCollection<Guid> incidentIds,
        Func<OperationalFailureIncident, IncidentTransitionResult> transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incidentIds);
        ArgumentNullException.ThrowIfNull(transition);

        var outcomes = new List<IncidentTransitionOutcome>(incidentIds.Count);
        foreach (var batch in incidentIds.Distinct().Chunk(TransitionBatchSize))
        {
            outcomes.AddRange(await TransitionBatchAsync(batch, transition, cancellationToken));
        }

        return outcomes;
    }

    public async Task<IReadOnlyList<Guid>> ListUnresolvedIdsByRootCauseAsync(string rootCauseKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCauseKey);

        return await Visible()
            .Where(i => i.RootCauseKey == rootCauseKey && i.TriageState != IncidentTriageState.Resolved)
            .OrderBy(i => i.FirstSeenUtc)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<OperationalFailureIncident> Items, int TotalCount)?> ListRelatedAsync(
        Guid incidentId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var rootCauseKey = await Visible()
            .Where(i => i.Id == incidentId)
            .Select(i => i.RootCauseKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (rootCauseKey is null)
        {
            return null;
        }

        var query = Visible().Where(i =>
            i.RootCauseKey == rootCauseKey && i.Id != incidentId && i.TriageState != IncidentTriageState.Resolved);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.LastSeenUtc)
            .ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<int> CountUnacknowledgedCriticalRootCausesAsync(CancellationToken cancellationToken = default) =>
        Visible()
            .Where(i => i.HighestSeverity == OperationalFailureSeverity.Critical && i.TriageState == IncidentTriageState.Open)
            .Select(i => i.RootCauseKey)
            .Distinct()
            .CountAsync(cancellationToken);

    /// <summary>
    /// One load and one save for the whole batch, which is what keeps a 1,000-incident root-cause
    /// resolve inside one request (SC-011). <c>RowVersion</c> moves on every counter bump, so a busy
    /// incident can fail the batch's save; the batch then falls back to one incident at a time.
    /// </summary>
    private async Task<IReadOnlyList<IncidentTransitionOutcome>> TransitionBatchAsync(
        Guid[] incidentIds, Func<OperationalFailureIncident, IncidentTransitionResult> transition, CancellationToken cancellationToken)
    {
        var incidents = await dbContext.OperationalFailureIncidents
            .Where(i => incidentIds.Contains(i.Id) && i.OccurrenceCount > 0)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        try
        {
            List<IncidentTransitionOutcome> outcomes = [.. incidentIds.Select(id => incidents.TryGetValue(id, out var incident)
                ? new IncidentTransitionOutcome(id, ToStatus(transition(incident)))
                : new IncidentTransitionOutcome(id, IncidentTransitionStatus.NotFound))];
            await dbContext.SaveChangesAsync(cancellationToken);
            return outcomes;
        }
        catch (DbUpdateException)
        {
            // SaveChanges is transactional, so nothing in the batch was written; retry each on its own.
            Detach(incidents.Values);
            var outcomes = new List<IncidentTransitionOutcome>(incidentIds.Length);
            foreach (var id in incidentIds)
            {
                outcomes.Add(await TransitionOneAsync(id, transition, cancellationToken));
            }

            return outcomes;
        }
        finally
        {
            Detach(incidents.Values);
        }
    }

    private async Task<IncidentTransitionOutcome> TransitionOneAsync(
        Guid incidentId, Func<OperationalFailureIncident, IncidentTransitionResult> transition, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var incident = await dbContext.OperationalFailureIncidents
                .FirstOrDefaultAsync(i => i.Id == incidentId && i.OccurrenceCount > 0, cancellationToken);
            if (incident is null)
            {
                return new IncidentTransitionOutcome(incidentId, IncidentTransitionStatus.NotFound);
            }

            try
            {
                if (transition(incident) == IncidentTransitionResult.AlreadyInState)
                {
                    return new IncidentTransitionOutcome(incidentId, IncidentTransitionStatus.AlreadyInState);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return new IncidentTransitionOutcome(incidentId, IncidentTransitionStatus.Applied);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxTransitionAttempts)
            {
                // Reloaded and re-applied to the row's current state on the next pass.
            }
            catch (DbUpdateConcurrencyException)
            {
                return new IncidentTransitionOutcome(incidentId, IncidentTransitionStatus.Conflict);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                // Only a reopen can trip UX_Incidents_GroupingKey_Unresolved: a newer incident already holds the key.
                dbContext.Entry(incident).State = EntityState.Detached;
                var newer = await FindUnresolvedAsync(incident.GroupingKey, cancellationToken);
                return new IncidentTransitionOutcome(incidentId, IncidentTransitionStatus.NewerIncidentOpen, newer);
            }
            finally
            {
                dbContext.Entry(incident).State = EntityState.Detached;
            }
        }
    }

    private static IncidentTransitionStatus ToStatus(IncidentTransitionResult result) =>
        result == IncidentTransitionResult.Applied ? IncidentTransitionStatus.Applied : IncidentTransitionStatus.AlreadyInState;

    private void Detach(IEnumerable<OperationalFailureIncident> incidents)
    {
        foreach (var incident in incidents)
        {
            dbContext.Entry(incident).State = EntityState.Detached;
        }
    }

    /// <summary>An incident is visible once its first occurrence has been counted (see <see cref="OpenAsync"/>).</summary>
    private IQueryable<OperationalFailureIncident> Visible() =>
        dbContext.OperationalFailureIncidents.AsNoTracking().Where(i => i.OccurrenceCount > 0);

    private Task<Guid?> FindUnresolvedAsync(string groupingKey, CancellationToken cancellationToken) =>
        dbContext.OperationalFailureIncidents
            .AsNoTracking()
            .Where(i => i.GroupingKey == groupingKey && i.TriageState != IncidentTriageState.Resolved)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Inserts the incident with zero counters (the join that follows counts the first occurrence).
    /// Losing the race to another process trips <c>UX_Incidents_GroupingKey_Unresolved</c>; the
    /// winner is then joined instead.
    /// </summary>
    private async Task<(Guid Id, bool Opened)> OpenAsync(IncidentAppendRequest request, CancellationToken cancellationToken)
    {
        var previous = await dbContext.OperationalFailureIncidents
            .AsNoTracking()
            .Where(i => i.GroupingKey == request.GroupingKey && i.TriageState == IncidentTriageState.Resolved)
            .OrderByDescending(i => i.ResolvedAtUtc)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var incident = OperationalFailureIncident.Open(
            request.GroupingKey,
            request.RootCauseKey,
            request.Engine,
            request.Operation,
            request.Kind,
            request.Severity,
            request.OccurredAtUtc,
            request.Reason,
            request.CorrelationId,
            request.ProviderId,
            request.ProviderName,
            request.Model,
            request.Subject?.Type,
            request.Subject?.Id,
            request.Subject?.Label,
            previous);

        dbContext.OperationalFailureIncidents.Add(incident);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.Entry(incident).State = EntityState.Detached;
            return (incident.Id, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            dbContext.Entry(incident).State = EntityState.Detached;
            var winner = await FindUnresolvedAsync(request.GroupingKey, cancellationToken)
                ?? throw new InvalidOperationException("The unresolved incident that won the insert race is no longer unresolved.", ex);
            return (winner, false);
        }
    }

    /// <summary>
    /// One transaction, so the stored-occurrence slot is only spent when the occurrence is written
    /// and a distinct count only moves with its participant row. The counter UPDATE takes the
    /// incident row's lock first, which serialises concurrent joins of the same incident.
    /// </summary>
    private async Task<OperationalFailureSeverity> JoinAsync(Guid incidentId, IncidentAppendRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var at = request.OccurredAtUtc;
        var reason = request.Reason;
        var correlationId = request.CorrelationId;
        var severity = request.Severity;

        await dbContext.OperationalFailureIncidents
            .Where(i => i.Id == incidentId)
            .ExecuteUpdateAsync(
                s =>
                {
                    s.SetProperty(i => i.OccurrenceCount, i => i.OccurrenceCount + 1)
                        .SetProperty(i => i.LastSeenUtc, i => i.LastSeenUtc > at ? i.LastSeenUtc : at)
                        .SetProperty(i => i.LatestReason, i => i.LastSeenUtc > at ? i.LatestReason : reason)
                        .SetProperty(i => i.LatestCorrelationId, i => i.LastSeenUtc > at ? i.LatestCorrelationId : correlationId);

                    // HighestSeverity is stored as a string, so "max" is spelled out from the known new value.
                    if (severity == OperationalFailureSeverity.Critical)
                    {
                        s.SetProperty(i => i.HighestSeverity, OperationalFailureSeverity.Critical);
                    }
                    else if (severity == OperationalFailureSeverity.Error)
                    {
                        s.SetProperty(
                            i => i.HighestSeverity,
                            i => i.HighestSeverity == OperationalFailureSeverity.Critical ? OperationalFailureSeverity.Critical : OperationalFailureSeverity.Error);
                    }
                },
                cancellationToken);

        var slotClaimed = await dbContext.OperationalFailureIncidents
            .Where(i => i.Id == incidentId && i.StoredOccurrenceCount < _maxStoredOccurrences)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.StoredOccurrenceCount, i => i.StoredOccurrenceCount + 1), cancellationToken);

        if (slotClaimed == 1)
        {
            var occurrence = OperationalFailureOccurrence.Create(
                incidentId,
                request.OccurredAtUtc,
                request.Severity,
                request.Kind,
                request.Engine,
                request.Operation,
                request.Reason,
                request.CorrelationId,
                request.References,
                request.ProviderName,
                request.Model,
                request.IsFailover,
                request.SourceIp);

            dbContext.OperationalFailureOccurrences.Add(occurrence);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.Entry(occurrence).State = EntityState.Detached;
        }

        if (!string.IsNullOrWhiteSpace(request.References.UserId)
            && await AddParticipantAsync(incidentId, IncidentParticipantType.User, request.References.UserId, at, cancellationToken))
        {
            await dbContext.OperationalFailureIncidents
                .Where(i => i.Id == incidentId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DistinctUserCount, i => i.DistinctUserCount + 1), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceIp)
            && await AddParticipantAsync(incidentId, IncidentParticipantType.Source, request.SourceIp, at, cancellationToken))
        {
            await dbContext.OperationalFailureIncidents
                .Where(i => i.Id == incidentId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DistinctSourceCount, i => i.DistinctSourceCount + 1), cancellationToken);
        }

        var highest = await dbContext.OperationalFailureIncidents
            .AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => i.HighestSeverity)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return highest;
    }

    /// <returns><see langword="true"/> when the participant was new to the incident.</returns>
    private async Task<bool> AddParticipantAsync(
        Guid incidentId, IncidentParticipantType type, string key, DateTime firstSeenUtc, CancellationToken cancellationToken)
    {
        var typeName = type.ToString();
        try
        {
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO [OperationalFailureIncidentParticipants] ([IncidentId], [ParticipantType], [ParticipantKey], [FirstSeenUtc])
                SELECT {incidentId}, {typeName}, {key}, {firstSeenUtc}
                WHERE NOT EXISTS (
                    SELECT 1 FROM [OperationalFailureIncidentParticipants] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [IncidentId] = {incidentId} AND [ParticipantType] = {typeName} AND [ParticipantKey] = {key})
                """,
                cancellationToken);
            return inserted == 1;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    /// <summary>Caller-supplied text is bounded to its column, so an over-long operation or model name never costs the record.</summary>
    private static IncidentAppendRequest Clip(IncidentAppendRequest request) => request with
    {
        Operation = Clip(request.Operation, 120)!,
        Reason = Clip(request.Reason, 500)!,
        CorrelationId = Clip(request.CorrelationId, 64)!,
        ProviderName = Clip(request.ProviderName, 100),
        Model = Clip(request.Model, 200),
        SourceIp = Clip(request.SourceIp, 45),
        Subject = request.Subject is null
            ? null
            : request.Subject with { Type = Clip(request.Subject.Type, 40)!, Label = Clip(request.Subject.Label, 200) },
        References = request.References with
        {
            UserId = Clip(request.References.UserId, 450),
            JobId = Clip(request.References.JobId, 100),
        },
    };

    private static string? Clip(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
