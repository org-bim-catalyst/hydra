using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>
/// specs/072 research D9. A singleton that opens its own DI scope per call: it is used by the
/// singleton Supertonic model and by voice requests running alongside other work on the request's
/// DbContext, and must share a DbContext with neither.
/// </summary>
public sealed class ScopedHostedModelLocator(IServiceScopeFactory scopeFactory) : IHostedModelLocator
{
    public async Task<HostedModelResolution> ResolveAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomModelRepository>();

        var completed = await repository.FindCompletedForRepositoryAsync(repositoryId, cancellationToken);
        if (completed.Count == 0)
        {
            return HostedModelResolution.NoRecord.Instance;
        }

        var available = completed.FirstOrDefault(m => m.Availability == CustomModelAvailability.Available);
        return available is null
            ? new HostedModelResolution.Unavailable($"The model deployed from {repositoryId} is marked unavailable in Custom Models.")
            : new HostedModelResolution.Available(available.Destination, available.Id);
    }
}
