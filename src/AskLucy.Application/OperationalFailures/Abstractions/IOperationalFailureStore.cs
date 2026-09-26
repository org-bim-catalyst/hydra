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

/// <summary>The admin list's filters (contracts/admin-operational-failures.md → List filters). The time range applies to <c>LastSeenUtc</c>.</summary>
public sealed record IncidentFilter(
    DateTime FromUtc,
    DateTime ToUtc,
    IncidentStateFilter State = IncidentStateFilter.Unresolved,
    OperationalFailureSeverity? Severity = null,
    OperationalFailureEngine? Engine = null,
    string? Provider = null,
    OperationalFailureKind? Kind = null,
    string? UserId = null);

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
}
