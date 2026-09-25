using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// Turns queued signals into stored incidents inside the background writer's scope (specs/074
/// research D3): sanitise, derive severity, key, append — one signal at a time, so one bad report
/// never costs the rest of its batch. Its own failures go to the log only; it has no route to the
/// recorder (FR-021).
/// </summary>
public sealed class OperationalFailureIngestor(
    IOperationalFailureStore store,
    IPublisher publisher,
    ILogger<OperationalFailureIngestor> logger)
{
    public async Task<IReadOnlyList<IncidentAppendResult>> IngestAsync(
        IReadOnlyList<OperationalFailureSignal> signals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var results = new List<IncidentAppendResult>(signals.Count);
        foreach (var signal in signals)
        {
            switch (signal)
            {
                case OperationalFailureReport report:
                    var result = await AppendAsync(report, cancellationToken);
                    if (result is not null)
                    {
                        results.Add(result);
                    }

                    break;
                case VoiceRecoveryReport recovery:
                    await IncrementRecoveryAsync(recovery, cancellationToken);
                    break;
            }
        }

        return results;
    }

    private async Task<IncidentAppendResult?> AppendAsync(OperationalFailureReport report, CancellationToken cancellationToken)
    {
        IncidentAppendResult result;
        var correlationId = string.IsNullOrWhiteSpace(report.CorrelationId) ? Guid.NewGuid().ToString("N") : report.CorrelationId;
        try
        {
            var request = new IncidentAppendRequest(
                OperationalFailureKeys.Grouping(report),
                OperationalFailureKeys.RootCause(report),
                report.Engine,
                report.Operation.Trim(),
                report.Kind,
                OperationalFailureSeverityPolicy.Classify(report.Engine, report.Kind, report.Outcome),
                report.OccurredAtUtc,
                ReasonFor(report),
                correlationId,
                report.ProviderId,
                report.ProviderName,
                report.Model,
                report.Subject,
                report.References,
                report.SourceIp,
                report.IsFailover);

            result = await store.AppendAsync(request, cancellationToken);
            OperationalFailureLog.OperationalFailureRecorded(logger, correlationId, report.Engine, report.Kind, result.IncidentId, result.Opened);
        }
        catch (Exception ex)
        {
            OperationalFailureLog.OperationalFailureRecordingFailed(logger, ex, correlationId, report.Engine, report.Kind);
            return null;
        }

        if (result is { Opened: true, Severity: OperationalFailureSeverity.Critical })
        {
            try
            {
                await publisher.Publish(
                    new CriticalIncidentOpened(result.IncidentId, report.Engine, report.Kind, report.ProviderName, correlationId),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                OperationalFailureLog.CriticalIncidentNotificationFailed(logger, ex, result.IncidentId, correlationId);
            }
        }

        return result;
    }

    private async Task IncrementRecoveryAsync(VoiceRecoveryReport recovery, CancellationToken cancellationToken)
    {
        try
        {
            if (!await store.IncrementRecoveryAsync(OperationalFailureKeys.Grouping(recovery), cancellationToken))
            {
                OperationalFailureLog.VoiceRecoveryIgnored(logger, recovery.CorrelationId, recovery.Kind);
            }
        }
        catch (Exception ex)
        {
            OperationalFailureLog.VoiceRecoveryRecordingFailed(logger, ex, recovery.CorrelationId, recovery.Kind);
        }
    }

    /// <summary>The sanitised reason; the exception type when none survives; the kind as a last resort.</summary>
    private static string ReasonFor(OperationalFailureReport report)
    {
        var reason = FailureReasonSanitizer.Sanitize(report.Reason);
        if (reason.Length > 0)
        {
            return reason;
        }

        return report.Exception is not null ? report.Exception.GetType().Name : report.Kind.ToString();
    }
}
