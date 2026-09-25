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
