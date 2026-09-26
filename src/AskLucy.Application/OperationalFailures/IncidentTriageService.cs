using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// specs/074 FR-024–FR-026b, research D19 — acknowledge, resolve and reopen, for one incident or for
/// every unresolved incident sharing a root cause. Concurrency is judged on state, not on
/// <c>RowVersion</c>: a transition whose precondition no longer holds is the conflict.
/// </summary>
public sealed class IncidentTriageService(IOperationalFailureStore store, TimeProvider timeProvider, ICurrentUserAccessor currentUser)
{
    private const string ChangedBySomeoneElse = "This incident was changed by someone else.";

    public Task AcknowledgeAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var (userId, now) = Actor();
        return TransitionOneAsync(incidentId, i => i.Acknowledge(userId, now), cancellationToken);
    }

    public Task ResolveAsync(Guid incidentId, string? note, CancellationToken cancellationToken)
    {
        var (userId, now) = Actor();
        return TransitionOneAsync(incidentId, i => i.Resolve(userId, now, note), cancellationToken);
    }

    public Task ReopenAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        _ = Actor();
        return TransitionOneAsync(incidentId, i => i.Reopen(), cancellationToken);
    }

    public Task<BulkTransitionResultDto> AcknowledgeRootCauseAsync(string rootCauseKey, CancellationToken cancellationToken)
    {
        var (userId, now) = Actor();
        return TransitionRootCauseAsync(rootCauseKey, i => i.Acknowledge(userId, now), cancellationToken);
    }

    public Task<BulkTransitionResultDto> ResolveRootCauseAsync(string rootCauseKey, string? note, CancellationToken cancellationToken)
    {
        var (userId, now) = Actor();
        return TransitionRootCauseAsync(rootCauseKey, i => i.Resolve(userId, now, note), cancellationToken);
    }

    private async Task TransitionOneAsync(
        Guid incidentId, Func<OperationalFailureIncident, IncidentTransitionResult> transition, CancellationToken cancellationToken)
    {
        var outcome = (await store.TransitionAsync([incidentId], transition, cancellationToken)).Single();
        switch (outcome.Status)
        {
            case IncidentTransitionStatus.Applied:
                return;
            case IncidentTransitionStatus.NotFound:
                throw new KeyNotFoundException($"Incident {incidentId} was not found.");
            case IncidentTransitionStatus.NewerIncidentOpen:
                throw new IncidentConflictException("A newer incident is already open for this cause.", outcome.NewerIncidentId);
            default:
                throw new IncidentConflictException(ChangedBySomeoneElse);
        }
    }

    /// <summary>
    /// Each incident succeeds or fails on its own. One already in the target state, or gone by the
    /// time it is reached, is skipped; one that kept changing underneath is reported as failed.
    /// </summary>
    private async Task<BulkTransitionResultDto> TransitionRootCauseAsync(
        string rootCauseKey, Func<OperationalFailureIncident, IncidentTransitionResult> transition, CancellationToken cancellationToken)
    {
        var incidentIds = await store.ListUnresolvedIdsByRootCauseAsync(rootCauseKey, cancellationToken);
        var outcomes = await store.TransitionAsync(incidentIds, transition, cancellationToken);

        var failed = outcomes
            .Where(o => o.Status is IncidentTransitionStatus.Conflict or IncidentTransitionStatus.NewerIncidentOpen)
            .Select(o => new BulkTransitionFailureDto(o.IncidentId, ChangedBySomeoneElse))
            .ToList();

        return new BulkTransitionResultDto(
            outcomes.Count,
            outcomes.Count(o => o.Status == IncidentTransitionStatus.Applied),
            outcomes.Count(o => o.Status is IncidentTransitionStatus.AlreadyInState or IncidentTransitionStatus.NotFound),
            failed);
    }

    private (string UserId, DateTime Now) Actor() =>
        (currentUser.UserId ?? throw new UnauthorizedAccessException(), timeProvider.GetUtcNow().UtcDateTime);
}
