using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;
using Element = AskLucy.Infrastructure.Boundaries.OverpassRelatedSiteBuildingProvider.OverpassElement;
using Point = AskLucy.Infrastructure.Boundaries.OverpassRelatedSiteBuildingProvider.OverpassGeometryPoint;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// specs/077 — reading BurJuman's related buildings out of Overpass, and tracing the outline of the
/// ones chosen.
/// </summary>
public sealed class RelatedSiteBuildingsTests
{
    private const double OriginLatitude = 25.2530;
    private const double OriginLongitude = 55.3030;
    private const double MetersPerDegreeLatitude = 111_320.0;
    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(OriginLatitude * Math.PI / 180);

    [Fact]
    public void Interpret_ReadsTheEnglishNameFirst_AndFindsATowerMappedOnlyInEnglish()
    {
        var buildings = OverpassRelatedSiteBuildingProvider.Interpret([
            Way(1, Rect(100, 0, 130, 30), ("building", "commercial"), ("name:en", "BurJuman Business Tower")),
            Way(2, Rect(100, 40, 130, 70), ("building", "hotel"), ("name", "برجمان أرجان"), ("name:en", "BurJuman Arjaan by Rotana")),
        ]);

        buildings.Select(b => b.Name).Should().Equal("BurJuman Business Tower", "BurJuman Arjaan by Rotana");
        buildings[1].Names.Should().Equal("BurJuman Arjaan by Rotana", "برجمان أرجان");
        buildings.Should().OnlyContain(b => b.Kind == SiteBoundaryMemberKind.Building);
    }

    [Fact]
    public void Interpret_MapsEachStationEntranceOntoTheUnnamedHallItServes()
    {
        var buildings = OverpassRelatedSiteBuildingProvider.Interpret([
            Way(4, Rect(-60, 0, -20, 20), ("building", "transportation")),
            Node(10, -70, 10, ("railway", "station"), ("station", "subway"), ("name", "BurJuman")),
            Node(11, -40, 57, ("railway", "station"), ("station", "subway"), ("name", "BurJuman"), ("name:en", "BurJuman Station")),
        ]);

        buildings.Should().ContainSingle().Which.Should().Match<RelatedSiteBuilding>(b =>
            b.Id == "osm_way_4" && b.Kind == SiteBoundaryMemberKind.TransportStation && b.Name == "BurJuman Metro Station");
    }

    [Fact]
    public void Interpret_DropsWhatHasNoNameOrNoOutline()
    {
        var buildings = OverpassRelatedSiteBuildingProvider.Interpret([
            Way(5, Rect(0, 0, 10, 10), ("building", "yes")),
            Node(12, 500, 500, ("railway", "station"), ("name", "Far Away")),
            new Element("way", 6, new Dictionary<string, string> { ["building"] = "yes", ["name"] = "Open" }, [P(0, 0), P(10, 0), P(10, 10)]),
        ]);

        buildings.Should().BeEmpty();
    }

    [Fact]
    public void BuildQuery_AsksForNamedBuildingsInBothLanguagesAndTheStations()
    {
        var query = OverpassRelatedSiteBuildingProvider.BuildQuery(new GeoPoint(OriginLatitude, OriginLongitude), 250);

        query.Should().Contain("(around:250,25.253,55.303)")
            .And.Contain("[\"name\"]").And.Contain("[\"name:en\"]")
            .And.Contain("node(around:250,25.253,55.303)[\"railway\"=\"station\"]");
    }

    [Fact]
    public void Union_BridgesTheSeamBetweenTheMallAndItsTower()
    {
        var rings = new RasterSiteFootprintUnion().Union([Ring(0, 0, 100, 100), Ring(100.5, 0, 130, 30)], 2.0);

        rings.Should().ContainSingle();
        GeometryMath.AreaSquareMeters(rings[0]).Should().BeApproximately(100 * 100 + 30 * 30, 250);
    }

    [Fact]
    public void Union_KeepsATowerAcrossTheStreetAsItsOwnOutline()
    {
        var rings = new RasterSiteFootprintUnion().Union([Ring(0, 0, 100, 100), Ring(0, 126, 30, 156)], 2.0);

        rings.Should().HaveCount(2);
    }

    private static Element Way(long id, IReadOnlyList<Point> geometry, params (string Key, string Value)[] tags) =>
        new("way", id, tags.ToDictionary(t => t.Key, t => t.Value), geometry);

    private static Element Node(long id, double east, double north, params (string Key, string Value)[] tags)
    {
        var point = P(east, north);
        return new Element("node", id, tags.ToDictionary(t => t.Key, t => t.Value), null, point.Lat, point.Lon);
    }

    private static IReadOnlyList<Point> Rect(double west, double south, double east, double north) =>
        [P(west, south), P(east, south), P(east, north), P(west, north), P(west, south)];

    private static IReadOnlyList<GeoPoint> Ring(double west, double south, double east, double north) =>
        [.. Rect(west, south, east, north).Select(p => new GeoPoint(p.Lat, p.Lon))];

    private static Point P(double east, double north) =>
        new(OriginLatitude + (north / MetersPerDegreeLatitude), OriginLongitude + (east / MetersPerDegreeLongitude));
}
