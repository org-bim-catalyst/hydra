using AskLucy.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Memory;

internal static partial class MemoryExtractionSweepJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to enqueue memory extraction for chat {UserChatId} — will retry next sweep")]
    public static partial void EnqueueFailed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>
/// Hangfire recurring job (tasks.md T033, research.md Decision 6) — a safety net for the per-turn
/// enqueue in <c>SendChatMessageCommandHandler</c>: finds conversations updated since their own
/// <c>UserChat.LastMemoryAnalyzedAtUtc</c> checkpoint that the per-turn path never got to (e.g. its
/// own enqueue call failed before Hangfire accepted the job) and re-enqueues
/// <see cref="IMemoryExtractionJob"/> for each. A simple recurring sweep with no framework-free
/// orchestration logic of its own — unlike <c>MemoryExtractionJob</c> itself, this stays in
/// <c>Infrastructure</c>, mirroring <c>DocumentStatisticsRecomputeJob</c>'s placement.
/// </summary>
public sealed class MemoryExtractionSweepJob(
    IUserChatRepository userChatRepository,
    IBackgroundJobClient backgroundJobClient,
    ILogger<MemoryExtractionSweepJob> logger)
{
    private const int BatchSize = 100;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var chats = await userChatRepository.ListNeedingMemoryAnalysisAsync(BatchSize, cancellationToken);

        foreach (var chat in chats)
        {
            try
            {
                backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(chat.Id, CancellationToken.None));
            }
            catch (Exception ex)
            {
                // One conversation failing to enqueue must not block the rest of the sweep — it
                // remains eligible (checkpoint unchanged) and is picked up again next cycle.
                MemoryExtractionSweepJobLog.EnqueueFailed(logger, chat.Id, ex);
            }
        }
    }
}
