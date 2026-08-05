using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Queries.GetProcessingHistory;

/// <summary>The append-only processing history entries, newest-first (FR-013, contracts/document-processing-api.md).</summary>
public sealed record DocumentProcessingLogDto(Guid Id, string EventType, string? Detail, DateTime OccurredAtUtc)
{
    public static DocumentProcessingLogDto FromEntity(DocumentProcessingLog log) => new(
        log.Id, log.EventType, log.Detail, log.OccurredAtUtc);
}
