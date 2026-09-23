using System.Collections.Concurrent;
using AskLucy.Application.CustomModels.Abstractions;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>specs/072 research D7. Singleton: one token source per job running in this process.</summary>
public sealed class CustomModelDeploymentCancellationRegistry : ICustomModelDeploymentCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> running = new();

    public CancellationToken Register(Guid customModelId)
    {
        var source = new CancellationTokenSource();
        var registered = running.AddOrUpdate(
            customModelId,
            source,
            (_, previous) =>
            {
                // A re-delivered job replaces a stale registration; the old run is already over.
                previous.Dispose();
                return source;
            });
        return registered.Token;
    }

    public bool TryCancel(Guid customModelId)
    {
        if (!running.TryGetValue(customModelId, out var source))
        {
            return false;
        }

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Unregistered between the lookup and the cancel: the job has already finished.
            return false;
        }
    }

    public void Unregister(Guid customModelId)
    {
        if (running.TryRemove(customModelId, out var source))
        {
            source.Dispose();
        }
    }
}
