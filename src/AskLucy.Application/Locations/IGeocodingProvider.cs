namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution contracts/geocoding-provider-contract.md — one resolved
/// candidate from the geospatial/geocoding data source. Importance is the source's own
/// popularity/relevance signal, used by the confidence algorithm in
/// <see cref="ILocationResolutionService"/>.
/// </summary>
public sealed record GeocodingCandidate(
    string LocationName,
    double Latitude,
    double Longitude,
    double Importance);

/// <summary>
/// specs/037-location-query-resolution — abstracts forward geocoding so spec 035's future
/// caching decorator can wrap this interface without modifying the implementation (FR-010).
/// </summary>
public interface IGeocodingProvider
{
    /// <summary>Resolves a free-text place query to zero or more candidates.</summary>
    /// <exception cref="GeocodingProviderUnavailableException">On provider failure (non-success HTTP, timeout, or malformed JSON).</exception>
    Task<IReadOnlyList<GeocodingCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by <see cref="IGeocodingProvider.SearchAsync"/> on any provider-level failure —
/// mirrors <c>WeatherProviderUnavailableException</c> shape; caught by
/// <see cref="ILocationResolutionService"/> and mapped to
/// <see cref="LocationResolutionOutcomeType.Unavailable"/>.
/// </summary>
public sealed class GeocodingProviderUnavailableException : Exception
{
    public GeocodingProviderUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
