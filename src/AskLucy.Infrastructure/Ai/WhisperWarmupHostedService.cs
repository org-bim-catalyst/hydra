using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Ai;

internal static partial class WhisperWarmupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Whisper model warm-up failed; it will load lazily on first use instead")]
    public static partial void WarmupFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Starts the Whisper model download/load in the background as soon as the app starts,
/// instead of leaving it to happen lazily on whichever request first calls the mic — that
/// first request would otherwise stall for the full one-time download+load.
/// </summary>
public sealed class WhisperWarmupHostedService(
    WhisperLocalTranscriptionProvider provider, ILogger<WhisperWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await provider.WarmUpAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                WhisperWarmupLog.WarmupFailed(logger, ex);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
