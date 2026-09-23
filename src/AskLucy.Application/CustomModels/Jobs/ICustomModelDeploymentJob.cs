namespace AskLucy.Application.CustomModels.Jobs;

/// <summary>specs/072 research D6. Enqueued through Hangfire's <c>IBackgroundJobClient</c> against this interface.</summary>
public interface ICustomModelDeploymentJob
{
    Task RunAsync(Guid customModelId, CancellationToken cancellationToken);
}
