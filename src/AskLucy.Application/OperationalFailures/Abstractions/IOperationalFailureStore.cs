using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>A sanitised, classified and keyed report, ready to append (research D3).</summary>
public sealed record IncidentAppendRequest(
    string GroupingKey,
    string RootCauseKey,
    OperationalFailureEngine Engine,
    string Operation,
    OperationalFailureKind Kind,
    OperationalFailureSeverity Severity,
    DateTime OccurredAtUtc,
    string Reason,
    string CorrelationId,
    Guid? ProviderId,
    string? ProviderName,
    string? Model,
    OperationalFailureSubject? Subject,
    OperationalFailureReferences References,
    string? SourceIp,
    bool IsFailover);

/// <summary>Which incident an append landed in, and whether it opened it (research D18).</summary>
public sealed record IncidentAppendResult(Guid IncidentId, bool Opened, OperationalFailureSeverity Severity);

/// <summary>The list filter's triage state; <see cref="Unresolved"/> is Open or Acknowledged.</summary>
public enum IncidentStateFilter
{
    Open,
    Acknowledged,
    Resolved,
    Unresolved,
}

/// <summary>
/// The admin list's filters (contracts/admin-operational-failures.md → List filters). The time
/// range applies to <c>LastSeenUtc</c>. Values within one multi-valued filter are OR-ed; an empty
/// or absent one does not filter.
/// </summary>
public sealed record IncidentFilter(
    DateTime FromUtc,
    DateTime ToUtc,
    IncidentStateFilter State = IncidentStateFilter.Unresolved,
    IReadOnlyCollection<OperationalFailureSeverity>? Severities = null,
    IReadOnlyCollection<OperationalFailureEngine>? Engines = null,
    string? Provider = null,
    IReadOnlyCollection<OperationalFailureKind>? Kinds = null,
    string? UserId = null);

/// <summary>What a triage transition did to one incident (research D19).</summary>
public enum IncidentTransitionStatus
{
    Applied,

    /// <summary>The transition's precondition no longer held, typically because someone else moved the incident first.</summary>
    AlreadyInState,

    /// <summary>No visible incident has the id.</summary>
    NotFound,

    /// <summary>The row kept changing under the transition until its retries ran out.</summary>
    Conflict,

    /// <summary>A reopen collided with a newer unresolved incident holding the same grouping key.</summary>
    NewerIncidentOpen,
}

public sealed record IncidentTransitionOutcome(Guid IncidentId, IncidentTransitionStatus Status, Guid? NewerIncidentId = null);

/// <summary>
/// Persistence of the operational failure trail. Methods are added by the phase that implements
/// them; nothing here is a stub.
/// </summary>
/// <remarks>
/// Every read hides an incident whose first join has not committed yet (<c>OccurrenceCount = 0</c>):
/// the append opens the incident before it counts the occurrence.
/// </remarks>
public interface IOperationalFailureStore
{
    /// <summary>
    /// Joins the unresolved incident with the request's grouping key, or opens one (a recurrence
    /// when a resolved one exists). Race-safe across processes. Counter updates are bookkeeping and
    /// leave the audit columns untouched.
    /// </summary>
    Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments <c>RecoveryCount</c> on the unresolved incident with <paramref name="groupingKey"/>.
    /// Returns <see langword="false"/> when none is unresolved; the recovery is then ignored (spec Edge Cases).
    /// </summary>
    Task<bool> IncrementRecoveryAsync(string groupingKey, CancellationToken cancellationToken = default);

    /// <summary>A page of incidents matching <paramref name="filter"/>, most recently seen first, and the total match count.</summary>
    Task<(IReadOnlyList<OperationalFailureIncident> Items, int TotalCount)> ListIncidentsAsync(
        IncidentFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<OperationalFailureIncident?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>A page of the incident's stored occurrences, newest first.</summary>
    Task<(IReadOnlyList<OperationalFailureOccurrence> Items, int TotalCount)> ListOccurrencesAsync(
        Guid incidentId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// The occurrences of <paramref name="incidentId"/> that reference the item, or
    /// <see langword="null"/> when the incident does not reference it at all (research D15). An
    /// incident whose subject is the item references it even with no occurrence naming it, so the
    /// list can be empty.
    /// </summary>
    Task<IReadOnlyList<OperationalFailureOccurrence>?> FindItemReferencesAsync(
        Guid incidentId, InvestigatedItemType itemType, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>How many unresolved incidents carry each root-cause key (FR-026b).</summary>
    Task<IReadOnlyDictionary<string, int>> CountUnresolvedByRootCauseAsync(
        IReadOnlyCollection<string> rootCauseKeys, CancellationToken cancellationToken = default);

    /// <summary>The incident's most recently added distinct users, newest first.</summary>
    Task<IReadOnlyList<string>> ListRecentUserIdsAsync(Guid incidentId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="transition"/> to each incident through the tracked aggregate, so the
    /// audit interceptor stamps who changed it (research D19). A row that changed between load and
    /// save — another admin, or an occurrence bumping its counters — is reloaded and the transition
    /// re-applied to its current state. Returns one outcome per distinct id, and never throws for a
    /// single incident's conflict.
    /// </summary>
    Task<IReadOnlyList<IncidentTransitionOutcome>> TransitionAsync(
        IReadOnlyCollection<Guid> incidentIds,
        Func<OperationalFailureIncident, IncidentTransitionResult> transition,
        CancellationToken cancellationToken = default);

    /// <summary>The ids of every unresolved incident with <paramref name="rootCauseKey"/> (FR-026b).</summary>
    Task<IReadOnlyList<Guid>> ListUnresolvedIdsByRootCauseAsync(string rootCauseKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the other unresolved incidents sharing the incident's root cause, most recently seen
    /// first; <see langword="null"/> when the incident itself is not found.
    /// </summary>
    Task<(IReadOnlyList<OperationalFailureIncident> Items, int TotalCount)?> ListRelatedAsync(
        Guid incidentId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Distinct root causes with at least one Open (unacknowledged) Critical incident — the nav badge (FR-026).</summary>
    Task<int> CountUnacknowledgedCriticalRootCausesAsync(CancellationToken cancellationToken = default);
}
