namespace AskLucy.Application.SiteAnalysis;

/// <summary>contracts/site-analysis-hub-events.md "SiteAnalysisResultReceived" — pushed once per validated finding, immediately after persistence.</summary>
public sealed record SiteAnalysisResultReceivedDto(
    Guid AnalysisId,
    Guid ResultId,
    Guid UserChatId,
    string AnalysisType,
    string NoticeText,
    DateTime GeneratedAtUtc);

/// <summary>contracts/site-analysis-hub-events.md "SiteAnalysisCompleted" — pushed exactly once per analysis (FR-027), when the last specialist settles. <see cref="NoticeText"/> is null when every specialist succeeded (FR-024's quiet is preserved).</summary>
public sealed record SiteAnalysisCompletedDto(
    Guid AnalysisId,
    Guid UserChatId,
    string Status,
    int SucceededCount,
    int ExpectedCount,
    string? NoticeText);
