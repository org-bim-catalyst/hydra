namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/052-solar-analysis research D4 — thrown by an <see cref="IBuildingFootprintProvider"/>
/// implementation when the underlying data source can't be reached, mirroring
/// <c>BoundaryProviderUnavailableException</c>. Lives in Application (alongside the interface that
/// documents it), not Infrastructure, so callers can catch it without referencing Infrastructure
/// (constitution §3 Dependency Rule). Mapped to <c>503 Service Unavailable</c> Problem Details by
/// <c>ProblemDetailsMiddleware</c> (contracts/building-footprints-endpoint.md).
/// </summary>
public sealed class BuildingProviderUnavailableException : Exception
{
    public BuildingProviderUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
