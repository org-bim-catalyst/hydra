using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Spatial.Overlay;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Spatial.Overlay;

public sealed class OverlayGeometryOperationTests
{
    private static readonly GeoPoint Origin = new(23, 58);
    private readonly OverlayGeometryEngine _engine = new(Origin);

    [Fact]
    public void ExecuteStep_ShouldProduceIntersectionUnionAndDirectionalDifference()
    {
        var left = Polygon("left", 0, 0, 0.002, 0.002);
        var right = Polygon("right", 0.001, 0.001, 0.003, 0.003);
        var sources = new Dictionary<string, IReadOnlyList<QueryResultFeature>>
        {
            ["left"] = [left],
            ["right"] = [right],
        };

        var intersection = _engine.ExecuteStep(new OverlayStep("i", OverlayOperationKind.Intersection,
            ["left", "right"], "intersection"), sources);
        var union = _engine.ExecuteStep(new OverlayStep("u", OverlayOperationKind.Union,
            ["left", "right"], "union"), sources);
        var difference = _engine.ExecuteStep(new OverlayStep("d", OverlayOperationKind.Difference,
            ["left", "right"], "difference"), sources);

        intersection.Should().ContainSingle();
        union.Should().ContainSingle();
        difference.Should().ContainSingle();
        intersection[0].Overlay!.Provenance.Should().BeEquivalentTo(["left", "right"]);
        _engine.AreaSquareMeters(intersection[0]).Should().BeLessThan(_engine.AreaSquareMeters(left));
        _engine.AreaSquareMeters(union[0]).Should().BeGreaterThan(_engine.AreaSquareMeters(left));
        _engine.AreaSquareMeters(difference[0]).Should().BeLessThan(_engine.AreaSquareMeters(left));
    }

    [Fact]
    public void ExecuteStep_SpatialIntersectionShouldSelectAndPreserveSourceFeatures()
    {
        var candidate = Polygon("parcel-1", 0, 0, 0.001, 0.001);
        var near = Polygon("zone-1", 0.0005, 0.0005, 0.0015, 0.0015);
        var far = Polygon("zone-2", 0.01, 0.01, 0.011, 0.011);
        var result = _engine.ExecuteStep(
            new OverlayStep("select", OverlayOperationKind.SpatialIntersection, ["parcels", "zones"],
                "selected", OverlaySpatialRelation.Intersects),
            new Dictionary<string, IReadOnlyList<QueryResultFeature>>
            {
                ["parcels"] = [candidate],
                ["zones"] = [near, far],
            });

        result.Should().ContainSingle();
        result[0].FeatureId.Should().Be("parcel-1");
        result[0].Geometry.Should().Be(candidate.Geometry);
    }

    [Fact]
    public void ToGeometry_ShouldRejectSelfIntersectingPolygon()
    {
        var bowTie = new QueryResultFeature("bad", "Bad polygon",
            new FeatureGeometry(FeatureGeometryKind.Polygon,
            [new(23, 58), new(23.002, 58.002), new(23, 58.002), new(23.002, 58), new(23, 58)]),
            new Dictionary<string, object?>());

        var act = () => _engine.ToGeometry(bowTie);

        act.Should().Throw<InvalidOperationException>().WithMessage("*invalid geometry*");
    }

    private static QueryResultFeature Polygon(string id, double south, double west, double north, double east) =>
        new(id, id,
            new FeatureGeometry(FeatureGeometryKind.Polygon,
            [new(Origin.Latitude + south, Origin.Longitude + west),
             new(Origin.Latitude + south, Origin.Longitude + east),
             new(Origin.Latitude + north, Origin.Longitude + east),
             new(Origin.Latitude + north, Origin.Longitude + west),
             new(Origin.Latitude + south, Origin.Longitude + west)]),
            new Dictionary<string, object?>());
}
