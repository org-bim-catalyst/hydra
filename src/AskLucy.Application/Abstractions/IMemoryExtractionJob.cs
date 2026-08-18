namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background extraction/classification for one conversation (spec.md FR-006, FR-006a, FR-006b,
/// FR-008; research.md Decisions 6/7/8). The concrete implementation, <c>MemoryExtractionJob</c>,
/// lives in <c>AskLucy.Application</c> (not <c>Infrastructure</c> — a deviation from plan.md's
/// originally-proposed location, discovered during <c>/speckit-implement</c>: the job is pure
/// orchestration over Application abstractions — no framework-specific code — exactly mirroring
/// <c>IDocumentProcessingPipeline</c>/<c>DocumentProcessingPipeline</c>'s established placement,
/// which is the closer precedent than the simpler <c>DocumentStatisticsRecomputeJob</c>-style
/// recurring sweep jobs that do live in Infrastructure). Enqueued via <c>IBackgroundJobClient</c>
/// against this interface, never the concrete type, so Hangfire resolves it through the container
/// (same idiom <c>DocumentProcessingPipeline</c> already uses).
/// </summary>
public interface IMemoryExtractionJob
{
    /// <summary>
    /// Analyzes <paramref name="userChatId"/>'s recent turns for candidate memories. Automatically
    /// retried by Hangfire's <c>[AutomaticRetry]</c> attribute on the implementation — the first use
    /// of that built-in mechanism in this codebase (research.md Decision 6) — with failures logged,
    /// never surfaced to the user, once retries are exhausted (FR-006b).
    /// </summary>
    Task RunAsync(Guid userChatId, CancellationToken cancellationToken = default);
}
