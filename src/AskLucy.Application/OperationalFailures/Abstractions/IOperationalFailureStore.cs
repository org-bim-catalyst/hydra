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

/// <summary>
/// Persistence of the operational failure trail. Methods are added by the phase that implements
/// them; nothing here is a stub.
/// </summary>
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
}
