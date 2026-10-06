using System.Collections.Concurrent;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>What one pass of the delivery worker did.</summary>
/// <param name="Claimed">How many deliveries were leased in this pass; the worker polls faster while there is work.</param>
/// <param name="Processed">How many ended in a new state.</param>
/// <param name="DeferredFor">Set when a channel ran out of send capacity: the worker waits this long before the next pass.</param>
public sealed record DeliveryBatchResult(int Claimed, int Processed, TimeSpan? DeferredFor);

/// <summary>
/// One pass of the delivery relay (research R4, R5): claim the highest-priority due delivery, process it in
/// its own scope, and repeat up to the batch size. Called by the delivery worker <c>BackgroundService</c>.
/// A delivery that had not reached its channel sender when the host stopped is handed back unsent; one
/// interrupted mid-send is left for the lease sweeper to record as ambiguous.
/// </summary>
public sealed class DeliveryProcessingService(
    IServiceScopeFactory scopeFactory,
    INotificationChannelRegistry channels,
    IOptions<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryProcessingService> logger)
{
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a claim may take to finish once a shutdown has begun. The claim is a statement that commits on
    /// the server; cancelling the client mid-flight can leave it committed with nobody told, and the delivery
    /// would sit <c>Sending</c> until its lease expired and then be recorded as ambiguous although nothing was
    /// ever sent. Letting an in-flight claim finish (it takes milliseconds) means the worker always knows what
    /// it holds, and the next loop check stops it before it sends.
    /// </summary>
    private static readonly TimeSpan ClaimGrace = TimeSpan.FromSeconds(10);

    /// <summary>Claimed deliveries not yet handed to a sender, by id, with the worker that holds them.</summary>
    private readonly ConcurrentDictionary<Guid, string> _unstarted = new();

    public async Task<DeliveryBatchResult> ProcessBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        var dispatch = options.Value.Dispatch;
        var batchSize = Math.Clamp(dispatch.BatchSize, 1, MaxBatchSize);
        var claimable = channels.AvailableChannels.Where(c => c != NotificationChannel.InApp).ToList();

        var claimed = 0;
        var processed = 0;
        TimeSpan? deferredFor = null;
        try
        {
            while (claimed < batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // One delivery at a time, claimed just before it is sent. A lease then covers only that
                // delivery's own send: claiming a whole batch up front would start every lease at once, and a
                // delivery waiting behind slow sends could outlive its lease unsent and be recorded as
                // ambiguous by the sweeper although nothing ever left the process.
                var id = await ClaimOneAsync(workerId, claimable, dispatch.LeaseMinutes);
                if (id is null)
                {
                    break;
                }

                claimed++;
                var (outcome, retryAfter) = await ProcessOneAsync(id.Value, workerId, cancellationToken);
                if (outcome == DeliveryProcessOutcome.Completed)
                {
                    processed++;
                }
                else if (outcome == DeliveryProcessOutcome.Deferred)
                {
                    // The channel is out of capacity: the processor has put this delivery back, and everything
                    // else due would only be deferred in turn. Let the worker wait until the limiter refills.
                    deferredFor = retryAfter;
                    break;
                }
            }
        }
        finally
        {
            // Normally nothing: each claim is processed or released as it goes. After a shutdown or an
            // unexpected failure this gives back the one that never reached the sender.
            await ReleaseUnstartedAsync(workerId, cancellationToken);
        }

        return new DeliveryBatchResult(claimed, processed, deferredFor);
    }

    private async Task<Guid?> ClaimOneAsync(string workerId, List<NotificationChannel> claimable, int leaseMinutes)
    {
        var now = Now();
        using var grace = new CancellationTokenSource(ClaimGrace);
        await using var scope = scopeFactory.CreateAsyncScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<INotificationRepository>()
            .ClaimDueDeliveriesAsync(workerId, claimable, now.AddMinutes(Math.Max(1, leaseMinutes)), batchSize: 1, now, grace.Token);
        if (claimed.Count == 0)
        {
            return null;
        }

        _unstarted[claimed[0]] = workerId;
        return claimed[0];
    }

    /// <summary>
    /// Hands back whatever this worker still holds unsent. Safe to call from the worker's
    /// <c>StopAsync</c> too, which is why it doesn't depend on the loop having run (standing rule 10).
    /// </summary>
    public async Task<int> ReleaseUnstartedAsync(string workerId, CancellationToken cancellationToken)
    {
        var ids = _unstarted.Where(p => p.Value == workerId).Select(p => p.Key).ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        // The caller's token is usually the cancelled one; the release gets its own short budget.
        using var budget = new CancellationTokenSource(ReleaseTimeout);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var released = await scope.ServiceProvider.GetRequiredService<INotificationRepository>()
                .ReleaseClaimsAsync(workerId, ids, Now(), budget.Token);

            foreach (var id in ids)
            {
                _unstarted.TryRemove(id, out _);
            }

            DeliveryLog.Released(logger, released, workerId);
            return released;
        }
        catch (Exception ex)
        {
            // The leases still expire; each delivery is then recorded as ambiguous and shows in the admin
            // view, so nothing is lost silently. The failure itself is logged here.
            DeliveryLog.ReleaseFailed(logger, ex, ids.Count, workerId);
            return 0;
        }
    }

    /// <summary>True when the failed attempt was recorded; false leaves the claim to be handed back (or to expire).</summary>
    private async Task<bool> RecordPreparationFailureAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().RecordPreparationFailureAsync(deliveryId, workerId, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Both the attempt and its recording failed. The claim is handed back unspent, so nothing is lost.
            DeliveryLog.ProcessFailed(logger, ex, deliveryId, sendStarted: false, "could not record the failed attempt either; giving the claim back");
            return false;
        }
    }

    private async Task<(DeliveryProcessOutcome Outcome, TimeSpan? RetryAfter)> ProcessOneAsync(
        Guid deliveryId, string workerId, CancellationToken cancellationToken)
    {
        DeliveryProcessor? processor = null;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            processor = scope.ServiceProvider.GetRequiredService<DeliveryProcessor>();
            var outcome = await processor.ProcessAsync(deliveryId, workerId, cancellationToken);

            // Once processed the delivery is no longer "unstarted", whatever state it ended in; a
            // deferral has already been put back by the processor.
            _unstarted.TryRemove(deliveryId, out _);
            return (outcome, processor.RetryAfter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Mid-send the outcome is unknown and the lease sweeper records it; before the send it is still unstarted.
            if (processor is { SendStarted: true })
            {
                _unstarted.TryRemove(deliveryId, out _);
            }

            throw;
        }
        catch (Exception ex)
        {
            var sendStarted = processor?.SendStarted ?? false;
            DeliveryLog.ProcessFailed(logger, ex, deliveryId, sendStarted,
                sendStarted
                    ? "the lease sweeper will record it as ambiguous"
                    : "recording the failed attempt and retrying on schedule");

            if (sendStarted)
            {
                // The message may have left, so the delivery is not touched: the sweeper records it as ambiguous (R5).
                _unstarted.TryRemove(deliveryId, out _);
            }
            else if (await RecordPreparationFailureAsync(deliveryId, workerId, cancellationToken))
            {
                _unstarted.TryRemove(deliveryId, out _);
            }

            return (DeliveryProcessOutcome.Failed, null);
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
