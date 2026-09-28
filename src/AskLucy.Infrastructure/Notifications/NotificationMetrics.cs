using System.Diagnostics.Metrics;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// The hub's instruments on meter <c>AskLucy.Notifications</c> (FR-057). The two gauges report the
/// last snapshot handed to <see cref="ReportBacklog"/> and <see cref="ReportUnread"/>, and nothing
/// until one arrives, so a collector never mistakes "not measured yet" for zero. The delivery
/// worker's health check owns those snapshots (US3), which keeps DB reads off the collection thread.
/// </summary>
public sealed class NotificationMetrics : INotificationMetrics
{
    public const string MeterName = "AskLucy.Notifications";

    private readonly Counter<long> _created;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _providerErrors;
    private readonly Histogram<double> _latency;

    private BacklogSnapshot? _backlog;
    private UnreadSnapshot? _unread;

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("notifications.created", description: "Notifications materialized.");
        _sent = meter.CreateCounter<long>("deliveries.sent", description: "Deliveries a channel accepted.");
        _failed = meter.CreateCounter<long>("deliveries.failed", description: "Delivery attempts that failed.");
        _retried = meter.CreateCounter<long>("deliveries.retried", description: "Deliveries scheduled for another attempt.");
        _deadLettered = meter.CreateCounter<long>("deliveries.dead_lettered", description: "Deliveries that exhausted their attempts.");
        _providerErrors = meter.CreateCounter<long>("deliveries.provider_errors", description: "Errors returned by a channel provider.");
        _latency = meter.CreateHistogram<double>("deliveries.latency_ms", unit: "ms", description: "Time from notification creation to a sent delivery.");

        meter.CreateObservableGauge("notifications.backlog", ObserveBacklog, description: "Outbox events and deliveries waiting to be processed.");
        meter.CreateObservableGauge("notifications.unread", ObserveUnread, description: "Unread in-center notifications across all users.");
    }

    public void NotificationCreated(NotificationCategory category, string type) =>
        _created.Add(1, new KeyValuePair<string, object?>("category", category.ToString()), new KeyValuePair<string, object?>("type", type));

    public void DeliverySent(NotificationChannel channel, TimeSpan latency)
    {
        _sent.Add(1, Channel(channel));
        _latency.Record(latency.TotalMilliseconds, Channel(channel));
    }

    public void DeliveryFailed(NotificationChannel channel, DeliveryFailureKind failureKind) =>
        _failed.Add(1, Channel(channel), FailureKind(failureKind));

    public void DeliveryRetried(NotificationChannel channel) => _retried.Add(1, Channel(channel));

    public void DeliveryDeadLettered(NotificationChannel channel) => _deadLettered.Add(1, Channel(channel));

    public void ProviderError(NotificationChannel channel, DeliveryFailureKind failureKind) =>
        _providerErrors.Add(1, Channel(channel), FailureKind(failureKind));

    /// <summary>Replaces the backlog snapshot the <c>notifications.backlog</c> gauge reports.</summary>
    public void ReportBacklog(long outboxEvents, long deliveries) =>
        Volatile.Write(ref _backlog, new BacklogSnapshot(outboxEvents, deliveries));

    /// <summary>Replaces the count the <c>notifications.unread</c> gauge reports.</summary>
    public void ReportUnread(long unread) => Volatile.Write(ref _unread, new UnreadSnapshot(unread));

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        if (Volatile.Read(ref _backlog) is not { } backlog)
        {
            yield break;
        }

        yield return new Measurement<long>(backlog.OutboxEvents, new KeyValuePair<string, object?>("queue", "outbox"));
        yield return new Measurement<long>(backlog.Deliveries, new KeyValuePair<string, object?>("queue", "deliveries"));
    }

    private IEnumerable<Measurement<long>> ObserveUnread()
    {
        if (Volatile.Read(ref _unread) is { } unread)
        {
            yield return new Measurement<long>(unread.Count);
        }
    }

    private static KeyValuePair<string, object?> Channel(NotificationChannel channel) => new("channel", channel.ToString());

    private static KeyValuePair<string, object?> FailureKind(DeliveryFailureKind kind) => new("failure_kind", kind.ToString());

    private sealed record BacklogSnapshot(long OutboxEvents, long Deliveries);

    private sealed record UnreadSnapshot(long Count);
}
