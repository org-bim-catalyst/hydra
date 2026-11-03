namespace AskLucy.Application.Overpass;

/// <summary>
/// specs/089-site-geometry-specialist — the platform's shared read path for OpenStreetMap features,
/// defined here (Application) so consumers never reference the Overpass SDK, HTTP client or query
/// language (constitution §3). The implementation reuses the endpoints, retries, mirrors and typed
/// unavailability SPEC-042 established for the boundary search.
/// </summary>
public interface IOverpassFeatureQueryProvider
{
    /// <summary>
    /// Runs one bounded query and returns every element that carries usable geometry. Never throws
    /// into the caller on an upstream failure — that maps to
    /// <see cref="AskLucy.Application.SiteBoundaries.BoundaryProviderUnavailableException"/>.
    /// Caller cancellation is rethrown.
    /// </summary>
    Task<IReadOnlyList<OverpassFeature>> QueryAsync(
        OverpassFeatureQuery query, CancellationToken cancellationToken = default);
}
