namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/069 research D4: moves everyone's items off a retired model and notifies them, after the
/// confirming save. Enqueued via <c>IBackgroundJobClient</c> against this interface so the
/// Application layer never references the concrete Hangfire job.
/// </summary>
public interface IModelDeprecationUserImpactJob
{
    Task RunAsync(Guid batchId, CancellationToken cancellationToken = default);
}
