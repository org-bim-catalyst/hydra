namespace AskLucy.Domain.SiteBoundaries;

/// <summary>specs/077 — what kind of structure a <see cref="SiteBoundaryMember"/> is.</summary>
public enum SiteBoundaryMemberKind
{
    Building,

    /// <summary>A station named after the site. Never included by default: a station serves the site, it is not part of it.</summary>
    TransportStation,
}

/// <summary>specs/077 — how a <see cref="SiteBoundaryMember"/> sits relative to the site's own outline.</summary>
public enum SiteBoundaryMemberRelation
{
    /// <summary>Shares a wall with the site, or stands within a couple of metres of it — one structure on the ground.</summary>
    Connected,

    /// <summary>Carries the site's name but stands apart from it, e.g. across a street.</summary>
    Nearby,
}

/// <summary>
/// specs/077 — a building that carries the site's name, found around a resolved site boundary:
/// BurJuman's office tower and Arjaan hotel (connected), the "Burjman Office Tower" across the
/// street and the BurJuman metro station (nearby). The user decides which ones the highlighted
/// site includes; <see cref="Included"/> records that choice.
/// </summary>
/// <param name="Id">The source's own id (e.g. <c>osm_way_259738494</c>) — stable across turns, so a choice can name it.</param>
/// <param name="Name">The name to show and speak.</param>
/// <param name="GapMeters">Shortest distance from the site's own outline; 0 when the two touch.</param>
/// <param name="Ring">The member's footprint, a closed ring.</param>
public sealed record SiteBoundaryMember(
    string Id,
    string Name,
    SiteBoundaryMemberKind Kind,
    SiteBoundaryMemberRelation Relation,
    double GapMeters,
    IReadOnlyList<GeoPoint> Ring,
    bool Included);
