namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Wakes the dispatcher and delivery worker right after a commit, instead of waiting for the next poll (research R3).</summary>
public interface INotificationWakeSignal
{
    /// <summary>A new outbox event committed.</summary>
    void PulseDispatcher();

    /// <summary>A new or reset delivery committed.</summary>
    void PulseDeliveryWorker();

    /// <summary>Completes when pulsed or after <paramref name="timeout"/>, whichever comes first.</summary>
    Task WaitForDispatcherAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Completes when pulsed or after <paramref name="timeout"/>, whichever comes first.</summary>
    Task WaitForDeliveryWorkerAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
