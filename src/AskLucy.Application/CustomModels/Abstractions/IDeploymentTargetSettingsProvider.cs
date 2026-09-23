namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D1 — <b>the Connectors swap point</b>. Every reader of the deployment target
/// goes through this interface. The only implementation today reads configuration and is
/// deliberately temporary (plan.md); replacing it with a connector-backed provider is the planned
/// path, not a design regression.
/// </summary>
public interface IDeploymentTargetSettingsProvider
{
    /// <summary><see langword="null"/> when deployment is not configured (FR-019).</summary>
    ValueTask<DeploymentTargetSettings?> GetAsync(CancellationToken cancellationToken = default);
}
