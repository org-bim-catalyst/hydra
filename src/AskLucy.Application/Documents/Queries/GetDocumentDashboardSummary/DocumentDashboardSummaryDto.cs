using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;

/// <summary>contracts/document-processing-api.md's dashboard `statistics` sub-object (FR-046).</summary>
public sealed record DocumentStatisticsSummaryDto(
    int TotalDocuments,
    long TotalStorageBytes,
    long? AverageProcessingDurationMs,
    IReadOnlyDictionary<string, int> FileTypeDistribution,
    IReadOnlyDictionary<string, int> LanguageDistribution)
{
    private static readonly IReadOnlyDictionary<string, int> EmptyDistribution = new Dictionary<string, int>();

    /// <summary>A zeroed snapshot — the recompute job hasn't run yet for this scope (e.g. a brand-new user), never an error (mirrors <c>GetDocumentPreview</c>'s "Unavailable, not a failure" pattern).</summary>
    public static readonly DocumentStatisticsSummaryDto Empty = new(0, 0, null, EmptyDistribution, EmptyDistribution);

    public static DocumentStatisticsSummaryDto FromEntity(DocumentStatistics statistics) => new(
        statistics.TotalDocuments,
        statistics.TotalStorageBytes,
        statistics.AverageProcessingDurationMs,
        JsonSerializer.Deserialize<Dictionary<string, int>>(statistics.FileTypeDistributionJson) ?? [],
        JsonSerializer.Deserialize<Dictionary<string, int>>(statistics.LanguageDistributionJson) ?? []);
}

public sealed record DocumentRetryQueueEntryDto(Guid DocumentId, string FileName, string FailureReason)
{
    public static DocumentRetryQueueEntryDto FromEntry(DocumentRetryQueueEntry entry) => new(entry.DocumentId, entry.FileName, entry.FailureReason);
}

/// <summary>contracts/document-processing-api.md's dashboard shape (FR-045, FR-045a, US6 AC1) — shared by the per-user and organization-wide (admin) endpoints.</summary>
public sealed record DocumentDashboardSummaryDto(
    int QueueDepth,
    int InProgressCount,
    int CompletedTodayCount,
    int FailedCount,
    IReadOnlyList<DocumentRetryQueueEntryDto> RetryQueue,
    DocumentStatisticsSummaryDto Statistics);
