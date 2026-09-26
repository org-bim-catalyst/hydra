using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>Stored incidents and occurrences as the store returns them; the counters are ingestion-owned, so they are set by reflection.</summary>
internal static class OperationalFailureTestData
{
    public static readonly DateTime Now = new(2026, 9, 22, 10, 15, 0, DateTimeKind.Utc);

    public static OperationalFailureIncident Incident(
        OperationalFailureEngine engine = OperationalFailureEngine.AiProvider,
        OperationalFailureKind kind = OperationalFailureKind.CredentialRejected,
        string rootCauseKey = "root-1",
        Guid? providerId = null,
        string? providerName = "OpenAI",
        string? subjectType = null,
        Guid? subjectId = null,
        string? subjectLabel = null,
        int occurrenceCount = 3,
        int distinctUserCount = 2)
    {
        var incident = OperationalFailureIncident.Open(
            $"group-{Guid.NewGuid():N}",
            rootCauseKey,
            engine,
            "Chat reply",
            kind,
            OperationalFailureSeverity.Critical,
            Now.AddMinutes(-30),
            "The provider rejected the credential",
            "corr-latest",
            providerId,
            providerName,
            "gpt-5",
            subjectType,
            subjectId,
            subjectLabel);
        incident.Id = Guid.NewGuid();
        incident.RowVersion = [1, 2, 3, 4];
        SetPrivate(incident, nameof(OperationalFailureIncident.OccurrenceCount), occurrenceCount);
        SetPrivate(incident, nameof(OperationalFailureIncident.StoredOccurrenceCount), occurrenceCount);
        SetPrivate(incident, nameof(OperationalFailureIncident.DistinctUserCount), distinctUserCount);
        return incident;
    }

    public static OperationalFailureOccurrence Occurrence(
        Guid incidentId,
        OperationalFailureReferences references,
        OperationalFailureEngine engine = OperationalFailureEngine.Chat,
        string? sourceIp = null,
        bool userErased = false,
        DateTime? occurredAtUtc = null)
    {
        var occurrence = OperationalFailureOccurrence.Create(
            incidentId,
            occurredAtUtc ?? Now,
            OperationalFailureSeverity.Error,
            OperationalFailureKind.CredentialRejected,
            engine,
            "Chat reply",
            "The provider rejected the credential",
            "corr-1",
            references,
            sourceIp: sourceIp);
        occurrence.Id = Guid.NewGuid();
        if (userErased)
        {
            SetPrivate(occurrence, nameof(OperationalFailureOccurrence.IsUserErased), true);
        }

        return occurrence;
    }

    public static void SetPrivate(object target, string property, object value) =>
        target.GetType().GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(target, [value]);
}
