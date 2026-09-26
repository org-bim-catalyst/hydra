using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// specs/077 — BurJuman laid out in local metres, the way OSM maps it: the mall, an office tower
/// sharing its wall, the Arjaan hotel 0.5 m off it, a same-named tower 26 m across the street, the
/// metro station 20 m away, and an unrelated hotel right next door.
/// </summary>
internal static class BurJumanSite
{
    private const double OriginLatitude = 25.2530;
    private const double OriginLongitude = 55.3030;
    private const double MetersPerDegreeLatitude = 111_320.0;

    public static readonly IReadOnlyList<GeoPoint> Mall = Rect(0, 0, 100, 100);

    public static readonly RelatedSiteBuilding OfficeTower = Building("osm_way_1", "BurJuman Business Tower", Rect(100, 0, 130, 30));

    public static readonly RelatedSiteBuilding ArjaanHotel = Building("osm_way_2", "BurJuman Arjaan by Rotana", Rect(100.5, 40, 130, 70));

    /// <summary>OSM's own misspelling, one letter off.</summary>
    public static readonly RelatedSiteBuilding TowerAcrossTheStreet = Building("osm_way_3", "Burjman Office Tower", Rect(0, 126, 30, 156));

    public static readonly RelatedSiteBuilding MetroStation = new(
        "osm_way_4", "BurJuman Metro Station", ["BurJuman Metro Station", "BurJuman"], SiteBoundaryMemberKind.TransportStation, Rect(-60, 0, -20, 20));

    public static readonly RelatedSiteBuilding UnrelatedNeighbour = Building("osm_way_5", "City Seasons Towers Hotel", Rect(100.2, 80, 120, 100));

    public static readonly RelatedSiteBuilding FarNamesake = Building("osm_way_6", "BurJuman Residences", Rect(300, 0, 330, 30));

    public static IReadOnlyList<RelatedSiteBuilding> Everything =>
        [OfficeTower, ArjaanHotel, TowerAcrossTheStreet, MetroStation, UnrelatedNeighbour, FarNamesake];

    public static IReadOnlyList<SiteBoundaryMember> Members =>
        SiteBoundaryMembershipService.Classify(Mall, Everything, SiteNameMatcher.CoresOf(["BurJuman Mall"]), []);

    public static ConfirmedSiteBoundaryData Boundary(IReadOnlyList<SiteBoundaryMember>? members = null) =>
        new("BurJuman Mall", OriginLatitude, OriginLongitude, Mall, GeometryMath.AreaSquareMeters(Mall), 0.9,
            BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "osm_way_100", [])
        {
            Members = members ?? [],
        };

    /// <summary>A closed rectangle, west/south/east/north in metres from the origin.</summary>
    public static IReadOnlyList<GeoPoint> Rect(double west, double south, double east, double north) =>
        [Point(west, south), Point(east, south), Point(east, north), Point(west, north), Point(west, south)];

    public static GeoPoint Point(double eastMeters, double northMeters)
    {
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(OriginLatitude * Math.PI / 180.0);
        return new GeoPoint(OriginLatitude + (northMeters / MetersPerDegreeLatitude), OriginLongitude + (eastMeters / metersPerDegreeLongitude));
    }

    private static RelatedSiteBuilding Building(string id, string name, IReadOnlyList<GeoPoint> ring) =>
        new(id, name, [name], SiteBoundaryMemberKind.Building, ring);
}
