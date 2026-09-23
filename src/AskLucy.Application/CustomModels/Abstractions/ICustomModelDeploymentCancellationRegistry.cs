namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D7. The in-process fast path that stops a running job within seconds. The
/// persisted <c>CancellationRequestedAtUtc</c> flag covers a job running in another process.
/// </summary>
public interface ICustomModelDeploymentCancellationRegistry
{
    /// <summary>Registers the running job and returns the token a cancel request signals.</summary>
    CancellationToken Register(Guid customModelId);

    /// <summary><see langword="true"/> when a job running in this process was signalled.</summary>
    bool TryCancel(Guid customModelId);

    void Unregister(Guid customModelId);
}
