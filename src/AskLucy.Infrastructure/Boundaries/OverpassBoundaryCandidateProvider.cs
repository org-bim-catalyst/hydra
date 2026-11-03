using AskLucy.Application.Overpass;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Overpass;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// specs/042-site-boundary-resolution research.md #2/#12 — OSM-backed implementation of
/// <see cref="IBoundaryCandidateProvider"/>. Since specs/089 it takes its features from the shared
/// <see cref="IOverpassFeatureQueryProvider"/>, which owns the transport (endpoint hedging,
/// retries, timeout, typed unavailability) for every Overpass consumer; this class owns only what a
/// boundary candidate is — the tag filters, the closed-ring rule, and the adjacent-parts merge.
///
/// <para>
/// Closed <c>way</c> elements and, for the curated-value tag filters, <c>multipolygon</c>
/// relations — the relation's outer ring is assembled by the shared provider. Relations were
/// skipped in v1, which silently lost every site mapped that way: The Dubai Mall is relation
/// 18195959, so it never became a candidate and a nearby sliver was highlighted instead
/// (2026-09-25).
/// </para>
/// </summary>
internal sealed class OverpassBoundaryCandidateProvider(
    IOverpassFeatureQueryProvider featureQueryProvider) : IBoundaryCandidateProvider
{
    /// <summary>
    /// OSM tag filters marking a way as a plausible site-boundary candidate — ported from the
    /// reference notebook's own curated <c>BOUNDARY_SEARCH_TAGS</c>, not a blanket "any value of
    /// this key" scan. <c>Values: null</c> means "any value" (matches the notebook's
    /// <c>landuse: True</c>/<c>tourism: True</c>/<c>natural: True</c>); a non-null list restricts
    /// to those exact values, exactly as the notebook restricts <c>leisure</c> to
    /// park/garden/nature_reserve/recreation_ground and <c>amenity</c> to institutional types.
    ///
    /// Corrected after a live production failure: an unrestricted <c>["leisure"]</c> filter also
    /// matched <c>leisure=playground</c>/<c>pitch</c>/<c>swimming_pool</c> — small sub-features
    /// *within* a park, not candidates for the park's own boundary — and one of them (closer to
    /// the geocoded center than the park's own way) won on proximity alone, since this codebase's
    /// deterministic name-matcher can't credit an OSM name tagged in a different script (e.g.
    /// Arabic "حديقة الصفا 2") against a Latin-script query ("Al Safa Park 2"). Restricting
    /// <c>leisure</c>/<c>amenity</c> to the notebook's own curated values excludes that entire
    /// class of wrong candidate at the source, rather than trying to out-score it after the fact.
    /// "building"/"boundary" stay excluded — see research.md #2 (query cost) and #7 (oversized
    /// administrative areas already penalized by geometry-quality) for why.
    ///
    /// <c>shop=mall</c> added 2026-09-25: malls are among the most-named sites in the city and
    /// match none of the other filters, so The Dubai Mall and BurJuman were never candidates and
    /// whatever landuse way lay nearest the pin was highlighted instead.
    ///
    /// <c>amenity=place_of_worship</c>/<c>theatre</c> and the <c>building</c> values for places of
    /// worship added 2026-09-26: Muscat's Royal Opera House (<c>amenity=theatre</c>) and Sultan
    /// Qaboos Grand Mosque (a <c>building=mosque</c> relation, its amenity on a separate node) were
    /// never candidates; around the mosque only unnamed orchards were left to choose from. Only these
    /// named-landmark values of <c>building</c> are queried; the cost concern above is for any value.
    /// </summary>
    private static readonly (string Key, string[]? Values)[] CandidateTagFilters =
    [
        ("leisure", ["park", "garden", "nature_reserve", "recreation_ground"]),
        ("landuse", null),
        ("amenity", ["school", "hospital", "university", "college", "place_of_worship", "theatre"]),
        ("building", ["mosque", "cathedral", "church", "temple"]),
        ("tourism", null),
        ("natural", null),
        ("shop", ["mall"]),
    ];

    public async Task<IReadOnlyList<BoundaryCandidate>> SearchAsync(
        GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default)
    {
        // Relations are queried only for the curated-value filters: an any-value relation query
        // pulls in district-scale landuse and whole water bodies, never the site a user named.
        var filters = CandidateTagFilters
            .Select(filter => new OverpassTagFilter(filter.Key, filter.Values, IncludeRelations: filter.Values is not null))
            .ToList();

        var features = await featureQueryProvider.QueryAsync(
            new OverpassFeatureQuery(center, radiusMeters, filters), cancellationToken);

        var candidates = features
            .Where(feature => feature.ElementType != "way" || OverpassGeometry.IsClosedRing(feature.Geometry))
            .Select(feature => MapFeatureToCandidate(feature, center))
            .ToList();

        return MergeAdjacentPartsOfOneSite(candidates, center);
    }

    /// <summary>
    /// OSM sometimes maps one site as several adjacent areas. BurJuman is two <c>shop=mall</c> ways
    /// ("BurJuman Mall" and "Bur Juman Shopping Center") that share a wall, so only one half was
    /// highlighted (2026-09-25). Two candidates are merged into one outline when they are the same
    /// kind of site, share at least one edge, and carry the same name once generic words are
    /// stripped. The name rule keeps genuinely separate neighbours apart: Al Safa Park 1 and
    /// Al Safa Park 2 are both parks, but "alsafapark1" is not "alsafapark2". Merging happens
    /// here, not in the scorer, because only OSM's shared nodes make "shares an edge" exact.
    /// </summary>
    private static List<BoundaryCandidate> MergeAdjacentPartsOfOneSite(List<BoundaryCandidate> candidates, GeoPoint center)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < candidates.Count && !merged; i++)
            {
                for (var j = i + 1; j < candidates.Count && !merged; j++)
                {
                    var union = TryMergeParts(candidates[i], candidates[j], center);
                    if (union is null)
                    {
                        continue;
                    }

                    candidates[i] = union;
                    candidates.RemoveAt(j);
                    merged = true;
                }
            }
        }

        return candidates;
    }

    private static BoundaryCandidate? TryMergeParts(BoundaryCandidate a, BoundaryCandidate b, GeoPoint center)
    {
        var kind = SiteKindOf(a.Tags);
        if (kind is null || kind != SiteKindOf(b.Tags) || !NameCoresOf(a.Tags).Overlaps(NameCoresOf(b.Tags)))
        {
            return null;
        }

        var ring = UnionOfEdgeSharingRings(a.Polygon.ExteriorRing, b.Polygon.ExteriorRing);
        if (ring is null)
        {
            return null;
        }

        return new BoundaryCandidate(
            Id: $"{a.Id}+{b.Id}",
            Polygon: new SiteBoundaryPolygon(ring),
            Source: a.Source,
            Name: a.Name.Length > 0 ? a.Name : b.Name,
            Tags: MergedTagsOf(a.Tags, b.Tags),
            DistanceToCenterMeters: GeometryMath.DistanceMeters(GeometryMath.Centroid(ring), center),
            AreaSquareMeters: GeometryMath.AreaSquareMeters(ring));
    }

    /// <summary>
    /// The first part's tags, plus any the second part adds. Names the second part carries that the
    /// first doesn't go into "alt_name" (semicolon-separated, as in OSM), so the site still answers
    /// to them: BurJuman's east part is the one tagged name:en "BurJuman Mall".
    /// </summary>
    private static Dictionary<string, string> MergedTagsOf(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        var tags = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var (key, value) in second)
        {
            tags.TryAdd(key, value);
        }

        var names = new List<string>();
        foreach (var tagSet in (IReadOnlyDictionary<string, string>[])[first, second])
        {
            foreach (var key in (string[])["name", "name:en", "alt_name"])
            {
                if (tagSet.TryGetValue(key, out var value))
                {
                    names.AddRange(value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        var primaryNames = new[] { tags.GetValueOrDefault("name"), tags.GetValueOrDefault("name:en") };
        var alternativeNames = names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !primaryNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (alternativeNames.Count > 0)
        {
            tags["alt_name"] = string.Join(";", alternativeNames);
        }
        else
        {
            tags.Remove("alt_name");
        }

        return tags;
    }

    /// <summary>The first query filter the tags match, as <c>key=value</c> — e.g. <c>shop=mall</c>.</summary>
    private static string? SiteKindOf(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var (key, values) in CandidateTagFilters)
        {
            if (tags.TryGetValue(key, out var value) && (values is null || values.Contains(value)))
            {
                return $"{key}={value}";
            }
        }

        return null;
    }

    private static readonly HashSet<string> GenericNameWords = ["the", "mall", "shopping", "center", "centre"];

    /// <summary>
    /// Each name the element carries, lower-cased with punctuation, spaces and generic words
    /// removed: "Bur Juman Shopping Center" and "BurJuman Mall" both become "burjuman". Cores
    /// shorter than four characters are dropped as too weak to prove two areas are one site.
    /// </summary>
    private static HashSet<string> NameCoresOf(IReadOnlyDictionary<string, string> tags)
    {
        var cores = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in (string[])["name", "name:en"])
        {
            if (!tags.TryGetValue(key, out var name))
            {
                continue;
            }

            var words = new List<string>();
            var word = new System.Text.StringBuilder();
            foreach (var ch in name.ToLowerInvariant().Append(' '))
            {
                if (char.IsLetterOrDigit(ch))
                {
                    word.Append(ch);
                }
                else if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }

            var core = string.Concat(words.Where(w => !GenericNameWords.Contains(w)));
            if (core.Length >= 4)
            {
                cores.Add(core);
            }
        }

        return cores;
    }

    /// <summary>
    /// The outline of two rings that share one or more edges. With both rings wound the same way,
    /// a shared edge runs a→b in one and b→a in the other; dropping those pairs removes the
    /// internal wall, and the remaining edges chain into the combined outline. Returns
    /// <see langword="null"/> when nothing is shared (touching at a single corner is not
    /// adjacency) or the leftover edges don't form exactly one closed ring.
    /// </summary>
    private static List<GeoPoint>? UnionOfEdgeSharingRings(IReadOnlyList<GeoPoint> first, IReadOnlyList<GeoPoint> second)
    {
        var edgesA = CounterClockwiseEdges(first);
        var edgesB = CounterClockwiseEdges(second);
        var reversedA = edgesA.Select(e => (e.To, e.From)).ToHashSet();
        var reversedB = edgesB.Select(e => (e.To, e.From)).ToHashSet();

        var remaining = edgesA.Where(e => !reversedB.Contains(e))
            .Concat(edgesB.Where(e => !reversedA.Contains(e)))
            .ToList();
        if (remaining.Count == edgesA.Count + edgesB.Count || remaining.Count < 3)
        {
            return null;
        }

        var next = new Dictionary<GeoPoint, GeoPoint>();
        foreach (var (from, to) in remaining)
        {
            if (!next.TryAdd(from, to))
            {
                return null;
            }
        }

        var start = remaining[0].From;
        var ring = new List<GeoPoint> { start };
        var current = start;
        while (ring.Count <= remaining.Count && next.TryGetValue(current, out var following))
        {
            ring.Add(following);
            current = following;
            if (current == start)
            {
                break;
            }
        }

        return current == start && ring.Count == remaining.Count + 1 ? ring : null;
    }

    private static List<(GeoPoint From, GeoPoint To)> CounterClockwiseEdges(IReadOnlyList<GeoPoint> closedRing)
    {
        var points = closedRing.Take(closedRing.Count - 1).ToList();
        var signedArea = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Count];
            signedArea += (p.Longitude * q.Latitude) - (q.Longitude * p.Latitude);
        }

        if (signedArea < 0)
        {
            points.Reverse();
        }

        return points.Select((p, i) => (p, points[(i + 1) % points.Count])).ToList();
    }

    private static BoundaryCandidate MapFeatureToCandidate(OverpassFeature feature, GeoPoint center)
    {
        var ring = feature.Geometry;
        var polygon = new SiteBoundaryPolygon(ring);
        var centroid = GeometryMath.Centroid(ring);

        return new BoundaryCandidate(
            Id: feature.Id,
            Polygon: polygon,
            Source: SiteBoundarySource.OsmBoundary,
            Name: feature.Tags.GetValueOrDefault("name") ?? feature.Tags.GetValueOrDefault("name:en") ?? string.Empty,
            Tags: feature.Tags,
            DistanceToCenterMeters: GeometryMath.DistanceMeters(centroid, center),
            AreaSquareMeters: GeometryMath.AreaSquareMeters(ring));
    }
}
