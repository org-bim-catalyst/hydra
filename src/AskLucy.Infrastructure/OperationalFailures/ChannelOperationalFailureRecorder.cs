using System.Threading.Channels;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.OperationalFailures;

/// <summary>
/// specs/074 research D2: the in-process seam every engine calls. <see cref="Record"/> only stamps
/// the report and offers it to a bounded channel, so it never awaits I/O and never slows the
/// request; <see cref="OperationalFailureWriterService"/> persists it later. A full queue drops
/// the report with a warning — the caller's own log line already holds the failure.
/// </summary>
public sealed class ChannelOperationalFailureRecorder : IOperationalFailureRecorder, IOperationalFailureQueue
{
    private readonly Channel<OperationalFailureSignal> _channel;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelOperationalFailureRecorder> _logger;

    public ChannelOperationalFailureRecorder(
        IOptions<OperationalFailuresOptions> options,
        ICorrelationIdAccessor correlation,
        TimeProvider timeProvider,
        ILogger<ChannelOperationalFailureRecorder> logger)
    {
        _correlation = correlation;
        _timeProvider = timeProvider;
        _logger = logger;

        // Wait mode makes TryWrite refuse rather than evict when full: the newest report is the one
        // dropped, and the drop is logged, instead of an older report vanishing unannounced.
        _channel = Channel.CreateBounded<OperationalFailureSignal>(new BoundedChannelOptions(OperationalFailuresOptions.Normalize(options.Value).QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<OperationalFailureSignal> Reader => _channel.Reader;

    public void Record(OperationalFailureReport report)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(report);
            Enqueue(report, report.Engine, report.Kind);
        }
        catch (Exception ex)
        {
            LogRecorderFailed(ex);
        }
    }

    public void RecordRecovery(VoiceRecoveryReport report)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(report);
            Enqueue(report, OperationalFailureEngine.Voice, report.Kind);
        }
        catch (Exception ex)
        {
            LogRecorderFailed(ex);
        }
    }

    /// <summary>The correlation id and time are taken here, on the caller's thread, because both are gone once the writer picks the report up.</summary>
    private void Enqueue(OperationalFailureSignal signal, OperationalFailureEngine engine, OperationalFailureKind kind)
    {
        var stamped = signal with
        {
            CorrelationId = _correlation.Current,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        if (!_channel.Writer.TryWrite(stamped))
        {
            OperationalFailureLog.OperationalFailureDropped(_logger, stamped.CorrelationId, engine, kind);
        }
    }

    private void LogRecorderFailed(Exception exception)
    {
        try
        {
            OperationalFailureLog.RecorderFailed(_logger, exception);
        }
        catch (Exception)
        {
            // The logger itself threw. There is nowhere left to report that, and Record's contract
            // (G2) is that it never throws into the caller's own failure handling — this is the one
            // place a failure is deliberately not surfaced further.
        }
    }
}
