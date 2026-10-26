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
    /// The first two are the cluster's own nodes, run by the same project against the same planet
    /// data — measured at 1-5 s while the balanced hostname in front of them was refusing
    /// requests outright.
    /// </para>
    /// <para>
    /// The French instance is last and is a genuinely independent host, added 2026-10-25 after a
    /// network that blocks every overpass-api.de address (DNS resolves, TCP 443 never connects)
    /// left boundary lookup with no reachable endpoint at all. It was measured at 1.1 s from that
    /// network while the other four timed out, and it carries worldwide data — verified against a
    /// Dubai query, not just a European one. Only full-planet instances belong here:
    /// <c>overpass.osm.ch</c> answers quickly but holds Switzerland alone, so using it would turn
    /// an honest "couldn't look it up" into a wrong "no boundary found" and an approximate circle.
    /// </para>
    /// </remarks>
    public IList<string> MirrorBaseUrls { get; set; } =
    [
        "https://z.overpass-api.de/api/",
        "https://lz4.overpass-api.de/api/",
        "https://overpass.openstreetmap.fr/api/",
    ];
}
