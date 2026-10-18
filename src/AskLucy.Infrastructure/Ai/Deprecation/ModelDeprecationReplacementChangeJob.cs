using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;

namespace AskLucy.Infrastructure.Ai.Deprecation;

/// <inheritdoc cref="IModelDeprecationReplacementChangeJob"/>
public sealed class ModelDeprecationReplacementChangeJob(UserImpactProcessor processor) : IModelDeprecationReplacementChangeJob
{
    public Task RunAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        processor.ProcessReplacementChangeAsync(batchId, cancellationToken);
}
