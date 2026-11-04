using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Overpass;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Overpass;

/// <summary>specs/090 — the POI query must read nodes as well as shapes, and must AND the constraints of a category (mosque = place_of_worship AND religion=muslim).</summary>
public sealed class OverpassPoiQueryProviderTests
{
    private static readonly PoiCategoryCatalog Catalog = new();

    private static PoiQuery QueryFor(string category, int radius = 10_000, int max = 200)
    {
        Catalog.TryResolve(category, out var definition).Should().BeTrue();
        return new PoiQuery(new GeoPoint(23.5, 58.3), radius, definition, max);
    }

    [Fact]
    public void BuildQuery_ShouldAskForNodes_WaysAndRelations_WithinTheRadius()
    {
        var query = OverpassPoiQueryProvider.BuildQuery(QueryFor("schools"));

        query.Should().Contain("node[\"amenity\"=\"school\"](around:10000,23.5,58.3);");
        query.Should().Contain("way[\"amenity\"=\"school\"](around:10000,23.5,58.3);");
        query.Should().Contain("relation[\"amenity\"=\"school\"](around:10000,23.5,58.3);");
        query.Should().Contain("out center 200;");
    }

    [Fact]
    public void BuildQuery_ShouldAndBothConstraints_ForMosques()
    {
        var query = OverpassPoiQueryProvider.BuildQuery(QueryFor("mosques"));

        query.Should().Contain("node[\"amenity\"=\"place_of_worship\"][\"religion\"=\"muslim\"]");
    }

    [Fact]
    public void BuildQuery_ShouldSearchTheWholeBoundingBox_WhenTheAreaHasOne()
    {
        Catalog.TryResolve("hospitals", out var definition).Should().BeTrue();

        // "all hospitals in Oman" — the country's own extent, not a 25 km circle around its centre.
        var query = OverpassPoiQueryProvider.BuildQuery(new PoiQuery(
            new GeoPoint(23.5, 58.3), 10_000, definition, 200,
            new PoiBoundingBox(16.6, 52.0, 26.5, 59.9)));

        query.Should().Contain("(16.6,52,26.5,59.9)");
        query.Should().NotContain("around:");
        query.Should().Contain("[timeout:50]");
    }

    [Fact]
    public void BuildQuery_ShouldSearchInsideTheAdministrativeArea_WhenTheNameIsKnown()
    {
        Catalog.TryResolve("hospitals", out var definition).Should().BeTrue();

        // A bounding box around Oman includes the UAE; an area filter does not.
        var query = OverpassPoiQueryProvider.BuildQuery(new PoiQuery(
            new GeoPoint(23.5, 58.3), 10_000, definition, 200,
            new PoiBoundingBox(16.6, 52.0, 26.5, 59.9), AreaName: "Oman"));

        query.Should().Contain("area[\"boundary\"=\"administrative\"][\"name:en\"=\"Oman\"]");
        query.Should().Contain("(area.searchArea)");
        query.Should().NotContain("(16.6,52,26.5,59.9)");
        query.Should().NotContain("around:");
    }

    [Fact]
    public void ThrowIfUnavailable_ShouldFail_WhenOverpassReturnedATimeoutRemark()
    {
        // Live: HTTP 200, elements [], remark "runtime error: Query timed out ..." — read as
        // "no schools in Oman" before this check existed.
        var response = new OverpassPoiQueryProvider.OverpassPoiResponse(
            [], "runtime error: Query timed out in \"query\" after 2 seconds.");

        var act = () => OverpassPoiQueryProvider.ThrowIfUnavailable(response);

        act.Should().Throw<BoundaryProviderUnavailableException>().WithMessage("*timed out*");
    }

    [Fact]
    public void ThrowIfUnavailable_ShouldAcceptAnEmptyResultWithNoRemark()
    {
        var response = new OverpassPoiQueryProvider.OverpassPoiResponse([], null);

        var act = () => OverpassPoiQueryProvider.ThrowIfUnavailable(response);

        act.Should().NotThrow();
    }

    [Fact]
    public void Interpret_ShouldReadNodeCoordinates()
    {
        var elements = new List<OverpassPoiQueryProvider.OverpassPoiElement>
        {
            new("node", 1, 23.6, 58.4, null, new Dictionary<string, string> { ["name"] = "Al Noor School" }),
        };

        var features = OverpassPoiQueryProvider.Interpret(elements, 200);

        features.Should().ContainSingle();
        features[0].Id.Should().Be("osm_node_1");
        features[0].Name.Should().Be("Al Noor School");
        features[0].Latitude.Should().Be(23.6);
        features[0].Longitude.Should().Be(58.4);
    }

    [Fact]
    public void Interpret_ShouldReadWayCentres_AndSkipElementsWithoutAPoint()
    {
        var elements = new List<OverpassPoiQueryProvider.OverpassPoiElement>
        {
            new("way", 2, null, null, new OverpassPoiQueryProvider.OverpassPoiCenter(23.7, 58.5),
                new Dictionary<string, string> { ["name:en"] = "Al Amal Hospital" }),
            new("relation", 3, null, null, null, new Dictionary<string, string>()),
        };

        var features = OverpassPoiQueryProvider.Interpret(elements, 200);

        features.Should().ContainSingle();
        features[0].Id.Should().Be("osm_way_2");
        features[0].Name.Should().Be("Al Amal Hospital");
    }

    [Fact]
    public void Interpret_ShouldPreferEnglishName_ThenLocalName()
    {
        var elements = new List<OverpassPoiQueryProvider.OverpassPoiElement>
        {
            new("node", 4, 23.6, 58.4, null,
                new Dictionary<string, string> { ["name"] = "مدرسة", ["name:en"] = "School" }),
            new("node", 5, 23.6, 58.4, null,
                new Dictionary<string, string> { ["name"] = "مدرسة" }),
        };

        var features = OverpassPoiQueryProvider.Interpret(elements, 200);

        features[0].Name.Should().Be("School");
        features[1].Name.Should().Be("مدرسة");
    }

    [Fact]
    public void Interpret_ShouldCapTheResultSet()
    {
        var elements = Enumerable.Range(0, 10)
            .Select(i => new OverpassPoiQueryProvider.OverpassPoiElement(
                "node", i, 23.5 + i, 58.3, null, new Dictionary<string, string> { ["name"] = $"P{i}" }))
            .ToList();

        OverpassPoiQueryProvider.Interpret(elements, 4).Should().HaveCount(4);
    }
}
