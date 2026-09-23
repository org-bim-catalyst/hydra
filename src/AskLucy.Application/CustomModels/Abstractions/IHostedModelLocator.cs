namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D9. Finds the deployed folder a hosted engine should load its model from.
/// Only <see cref="Domain.CustomModels.CustomModelDeploymentState.Completed"/>, non-deleted records
/// count (FR-039), and repository ids match ignoring case. Nothing is cached: a change of
/// availability is seen by the next call (FR-038).
/// </summary>
public interface IHostedModelLocator
{
    Task<HostedModelResolution> ResolveAsync(string repositoryId, CancellationToken cancellationToken = default);
}

/// <summary>Where a hosted engine's model comes from.</summary>
public abstract record HostedModelResolution
{
    private HostedModelResolution()
    {
    }

    /// <summary>No completed deployment exists for the repository, so the engine keeps its configured folder.</summary>
    public sealed record NoRecord : HostedModelResolution
    {
        public static NoRecord Instance { get; } = new();
    }

    /// <summary>A completed deployment is Available. <paramref name="RelativeDirectory"/> is its destination, relative to the content root.</summary>
    public sealed record Available(string RelativeDirectory, Guid CustomModelId) : HostedModelResolution;

    /// <summary>Completed deployments exist but none is Available, so the engine must not run.</summary>
    public sealed record Unavailable(string Reason) : HostedModelResolution;
}
