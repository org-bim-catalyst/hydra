using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

public enum DocumentStatisticsScope
{
    User,
    Organization,
}

/// <summary>
/// Periodically computed, denormalized aggregate metrics powering the processing dashboard
/// (FR-046, FR-045a, data-model.md). Refreshed on a short interval by a Hangfire recurring job,
/// not synchronously on every write — SC-011 only requires 5-second accuracy, and several of
/// these aggregates are expensive to maintain transactionally at 1M-document scale (SC-004).
/// </summary>
public sealed class DocumentStatistics : BaseEntity
{
    public DocumentStatisticsScope Scope { get; private set; }

    /// <summary>Null for <see cref="DocumentStatisticsScope.Organization"/>-scoped rows.</summary>
    public string? OwnerId { get; private set; }

    public int TotalDocuments { get; private set; }

    public long TotalStorageBytes { get; private set; }

    public long? AverageProcessingDurationMs { get; private set; }

    /// <summary>e.g. <c>{ "Pdf": 120, "Docx": 45 }</c> — a small, dashboard-only breakdown; not a normalized table since it's never queried per-file-type in isolation.</summary>
    public string FileTypeDistributionJson { get; private set; } = "{}";

    public string LanguageDistributionJson { get; private set; } = "{}";

    public DateTime ComputedAtUtc { get; private set; }

    private DocumentStatistics()
    {
        // Required by EF Core materialization.
    }

    public static DocumentStatistics CreateForUser(string ownerId, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Scope = DocumentStatisticsScope.User,
            OwnerId = ownerId,
            ComputedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };

    public static DocumentStatistics CreateForOrganization(string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Scope = DocumentStatisticsScope.Organization,
            OwnerId = null,
            ComputedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };

    /// <summary>Overwrites every aggregate field with a freshly computed snapshot (called by the recompute job) — this row is a cache, not an event-sourced ledger.</summary>
    public void Refresh(int totalDocuments, long totalStorageBytes, long? averageProcessingDurationMs, string fileTypeDistributionJson, string languageDistributionJson, string actor)
    {
        TotalDocuments = totalDocuments;
        TotalStorageBytes = totalStorageBytes;
        AverageProcessingDurationMs = averageProcessingDurationMs;
        FileTypeDistributionJson = fileTypeDistributionJson;
        LanguageDistributionJson = languageDistributionJson;
        ComputedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
