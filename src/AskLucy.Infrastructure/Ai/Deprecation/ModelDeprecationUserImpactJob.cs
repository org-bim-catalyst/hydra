using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;

namespace AskLucy.Infrastructure.Ai.Deprecation;

/// <inheritdoc cref="IModelDeprecationUserImpactJob"/>
public sealed class ModelDeprecationUserImpactJob(UserImpactProcessor processor) : IModelDeprecationUserImpactJob
{
    public Task RunAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        processor.ProcessAsync(batchId, cancellationToken);
}
