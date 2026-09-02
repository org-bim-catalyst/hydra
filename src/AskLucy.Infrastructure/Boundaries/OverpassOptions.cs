namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// specs/042-site-boundary-resolution — configuration for the OSM Overpass boundary-candidate
/// provider. Bound from the "Boundaries:Overpass" appsettings section, mirroring
/// <c>GeocodingOptions</c>.
/// </summary>
public sealed class OverpassOptions
{
    public const string SectionName = "Boundaries:Overpass";

    public string SearchBaseUrl { get; set; } = "https://overpass-api.de/api/";

    /// <summary>
    /// Tried in order after <see cref="SearchBaseUrl"/> fails, before giving up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>overpass-api.de</c> is a load balancer in front of a cluster, and it is the part that
    /// saturates: on 2026-08-31 it answered 504 Gateway Timeout three times in a row, ~10 s apart,
    /// leaving a site with no boundary at all, and later returned 429 Too Many Requests on every
    /// attempt. Retrying the same saturated entry point is not a strategy.
    /// </para>
    /// <para>
    /// These two are the cluster's own nodes, run by the same project against the same planet
    /// data — measured at 1-5 s while the balanced hostname in front of them was refusing
    /// requests outright. Third-party mirrors were tried first and rejected: kumi.systems and
    /// private.coffee both timed out, osm.jp serves an invalid certificate, and a mirror that
    /// hangs costs the whole 30 s client timeout before the next one is even attempted.
    /// </para>
    /// </remarks>
    public IList<string> MirrorBaseUrls { get; set; } =
    [
        "https://z.overpass-api.de/api/",
        "https://lz4.overpass-api.de/api/",
    ];
}
