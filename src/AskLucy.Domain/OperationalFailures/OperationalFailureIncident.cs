using AskLucy.Domain.Common;

namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// One grouped failure (specs/074 FR-018): every occurrence sharing a <see cref="GroupingKey"/>
/// while the incident is unresolved joins it. The only mutable part of the trail. Triage
/// transitions go through the tracked aggregate, so the audit interceptor stamps the acting
/// administrator; occurrence counters are set-based bookkeeping written by the store. A new
/// occurrence never changes <see cref="TriageState"/>.
/// </summary>
public sealed class OperationalFailureIncident : BaseEntity
{
    public const int MaxResolutionNoteLength = 500;

    public string GroupingKey { get; private set; } = string.Empty;

    public string RootCauseKey { get; private set; } = string.Empty;

    public OperationalFailureEngine Engine { get; private set; }

    public string Operation { get; private set; } = string.Empty;

    public Guid? ProviderId { get; private set; }

    public string? ProviderName { get; private set; }

    public string? Model { get; private set; }

    public OperationalFailureKind Kind { get; private set; }

    public string? SubjectType { get; private set; }

    public Guid? SubjectId { get; private set; }

    public string? SubjectLabel { get; private set; }

    public OperationalFailureSeverity HighestSeverity { get; private set; }

    public DateTime FirstSeenUtc { get; private set; }

    public DateTime LastSeenUtc { get; private set; }

    /// <summary>Every occurrence, including those beyond the storage cap (FR-022).</summary>
    public int OccurrenceCount { get; private set; }

    public int StoredOccurrenceCount { get; private set; }

    public int DistinctUserCount { get; private set; }

    public int DistinctSourceCount { get; private set; }

    public int RecoveryCount { get; private set; }

    public string LatestReason { get; private set; } = string.Empty;

    public string LatestCorrelationId { get; private set; } = string.Empty;

    public IncidentTriageState TriageState { get; private set; }

    public string? AcknowledgedByUserId { get; private set; }

    public DateTime? AcknowledgedAtUtc { get; private set; }

    public string? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public string? ResolutionNote { get; private set; }

    public Guid? RecurrenceOfIncidentId { get; private set; }

    public bool IsRecurrence => RecurrenceOfIncidentId is not null;

    private OperationalFailureIncident()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// Opens an incident with zero counters. The store applies the first occurrence exactly as it
    /// applies every later one, so there is a single counting path.
    /// </summary>
    public static OperationalFailureIncident Open(
        string groupingKey,
        string rootCauseKey,
        OperationalFailureEngine engine,
        string operation,
        OperationalFailureKind kind,
        OperationalFailureSeverity severity,
        DateTime occurredAtUtc,
        string reason,
        string correlationId,
        Guid? providerId = null,
        string? providerName = null,
        string? model = null,
        string? subjectType = null,
        Guid? subjectId = null,
        string? subjectLabel = null,
        Guid? recurrenceOfIncidentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCauseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return new OperationalFailureIncident
        {
            Id = Guid.CreateVersion7(),
            GroupingKey = groupingKey,
            RootCauseKey = rootCauseKey,
            Engine = engine,
            Operation = operation,
            Kind = kind,
            HighestSeverity = severity,
            FirstSeenUtc = occurredAtUtc,
            LastSeenUtc = occurredAtUtc,
            LatestReason = reason,
            LatestCorrelationId = correlationId,
            ProviderId = providerId,
            ProviderName = providerName,
            Model = model,
            SubjectType = subjectType,
            SubjectId = subjectId,
            SubjectLabel = subjectLabel,
            RecurrenceOfIncidentId = recurrenceOfIncidentId,
            TriageState = IncidentTriageState.Open,
        };
    }

    public IncidentTransitionResult Acknowledge(string userId, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (TriageState != IncidentTriageState.Open)
        {
            return IncidentTransitionResult.AlreadyInState;
        }

        TriageState = IncidentTriageState.Acknowledged;
        AcknowledgedByUserId = userId;
        AcknowledgedAtUtc = nowUtc;
        return IncidentTransitionResult.Applied;
    }

    public IncidentTransitionResult Resolve(string userId, DateTime nowUtc, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxResolutionNoteLength })
        {
            throw new DomainRuleViolationException($"A resolution note can be at most {MaxResolutionNoteLength} characters.");
        }

        if (TriageState == IncidentTriageState.Resolved)
        {
            return IncidentTransitionResult.AlreadyInState;
        }

        TriageState = IncidentTriageState.Resolved;
        ResolvedByUserId = userId;
        ResolvedAtUtc = nowUtc;
        ResolutionNote = trimmed;
        return IncidentTransitionResult.Applied;
    }

    /// <summary>
    /// Back to Open, with the acknowledge and resolve fields cleared. The caller first checks that
    /// no newer unresolved incident holds the same key, which the filtered unique index would reject.
    /// </summary>
    public IncidentTransitionResult Reopen()
    {
        if (TriageState != IncidentTriageState.Resolved)
        {
            return IncidentTransitionResult.AlreadyInState;
        }

        TriageState = IncidentTriageState.Open;
        AcknowledgedByUserId = null;
        AcknowledgedAtUtc = null;
        ResolvedByUserId = null;
        ResolvedAtUtc = null;
        ResolutionNote = null;
        return IncidentTransitionResult.Applied;
    }
}
