namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — thrown by an <see cref="IBoundaryCandidateProvider"/>
/// implementation when the underlying data source can't be reached, mirroring
/// <c>GeocodingProviderUnavailableException</c>. Lives in Application (alongside the interface
/// that documents it), not Infrastructure, so <see cref="BoundaryResolutionService"/> can catch
/// it without referencing Infrastructure (constitution §3 Dependency Rule).
/// </summary>
public sealed class BoundaryProviderUnavailableException : Exception
{
    public BoundaryProviderUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
