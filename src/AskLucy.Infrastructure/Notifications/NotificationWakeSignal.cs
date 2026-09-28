using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// specs/067 research R3. One binary semaphore per worker: any number of pulses before a wait
/// collapse into a single wake-up, which is all a worker needs because each wake drains its queue.
/// </summary>
public sealed class NotificationWakeSignal : INotificationWakeSignal, IDisposable
{
    private readonly SemaphoreSlim _dispatcher = new(0, 1);
    private readonly SemaphoreSlim _deliveryWorker = new(0, 1);

    public void PulseDispatcher() => Pulse(_dispatcher);

    public void PulseDeliveryWorker() => Pulse(_deliveryWorker);

    public Task WaitForDispatcherAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _dispatcher.WaitAsync(timeout, cancellationToken);

    public Task WaitForDeliveryWorkerAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _deliveryWorker.WaitAsync(timeout, cancellationToken);

    public void Dispose()
    {
        _dispatcher.Dispose();
        _deliveryWorker.Dispose();
    }

    private static void Pulse(SemaphoreSlim semaphore)
    {
        if (semaphore.CurrentCount > 0)
        {
            return;
        }

        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // A concurrent pulse got there first; the worker is already due to wake.
        }
    }
}
