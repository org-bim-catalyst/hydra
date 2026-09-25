using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.OperationalFailures;

/// <summary>
/// specs/074 research D2: the channel's single reader. Each batch gets its own DI scope — and so its
/// own DbContext — and a failed batch is logged report by report, never retried and never
/// re-recorded (FR-021), so the loop always moves on. On shutdown it keeps writing what is queued
/// until <see cref="DrainTimeout"/>, then logs how many reports it had to abandon.
/// </summary>
internal sealed class OperationalFailureWriterService(
    IOperationalFailureQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<OperationalFailuresOptions> options,
    ILogger<OperationalFailureWriterService> logger) : BackgroundService
{
    private readonly int _batchSize = OperationalFailuresOptions.Normalize(options.Value).WriterBatchSize;

    /// <summary>Cancelled <see cref="DrainTimeout"/> after shutdown starts; bounds the batch in flight and the drain.</summary>
    private readonly CancellationTokenSource _abandon = new();

    internal TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteNextBatchAsync(_abandon.Token);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: StopAsync drains what is left.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _abandon.CancelAfter(DrainTimeout);
        await base.StopAsync(cancellationToken);

        // The drain lives here, not at the end of ExecuteAsync: a host that stops before the
        // framework has started ExecuteAsync never runs it at all, and the queue would be lost
        // without a trace. If the host stopped waiting while a batch is still in flight, that batch
        // still owns the single reader, so only the count is reported.
        if (ExecuteTask is null or { IsCompleted: true })
        {
            while (!_abandon.IsCancellationRequested && queue.Reader.TryPeek(out _))
            {
                await WriteNextBatchAsync(_abandon.Token);
            }
        }

        if (queue.Reader.Count > 0)
        {
            OperationalFailureLog.ReportsAbandonedOnShutdown(logger, queue.Reader.Count);
        }
    }

    public override void Dispose()
    {
        _abandon.Dispose();
        base.Dispose();
    }

    private async Task WriteNextBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<OperationalFailureSignal>(_batchSize);
        while (batch.Count < _batchSize && queue.Reader.TryRead(out var signal))
        {
            batch.Add(signal);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ingestor = scope.ServiceProvider.GetRequiredService<OperationalFailureIngestor>();
            await ingestor.IngestAsync(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            // The ingestor already isolates each report's own failure; reaching here means the batch
            // as a whole failed (the scope, or the ingestor itself). Every report gets its own line,
            // with its correlation id, so none is lost without trace.
            foreach (var signal in batch)
            {
                LogLost(signal, ex);
            }
        }
    }

    private void LogLost(OperationalFailureSignal signal, Exception exception)
    {
        switch (signal)
        {
            case OperationalFailureReport report:
                OperationalFailureLog.OperationalFailureRecordingFailed(logger, exception, report.CorrelationId, report.Engine, report.Kind);
                break;
            case VoiceRecoveryReport recovery:
                OperationalFailureLog.VoiceRecoveryRecordingFailed(logger, exception, recovery.CorrelationId, recovery.Kind);
                break;
        }
    }
}
