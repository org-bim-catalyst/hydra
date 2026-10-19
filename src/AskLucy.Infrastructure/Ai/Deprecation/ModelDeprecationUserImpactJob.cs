using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using Hangfire;

namespace AskLucy.Infrastructure.Ai.Deprecation;

/// <inheritdoc cref="IModelDeprecationUserImpactJob"/>
public sealed class ModelDeprecationUserImpactJob(UserImpactProcessor processor) : IModelDeprecationUserImpactJob
{
    /// <summary>
    /// Serialized per batch id: the apply command and the sweep can both enqueue the same batch, and two runs
    /// switching the same items would collide on their row versions.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public Task RunAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        processor.ProcessAsync(batchId, cancellationToken);
}
