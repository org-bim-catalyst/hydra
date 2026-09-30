using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>specs/079 T019 — validity, union area and the drift check, in metres near Muscat.</summary>
public sealed class NtsSiteRingGeometryTests
{
    private const double Lat = 23.59;
    private const double Lon = 58.40;
    private const double MetersPerDegreeLatitude = 111_320.0;

    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(Lat * Math.PI / 180);

    private readonly NtsSiteRingGeometry _geometry = new();

    /// <summary>A rectangle whose south-west corner is (eastMeters, northMeters) from the origin.</summary>
    private static IReadOnlyList<GeoPoint> Rectangle(double eastMeters, double northMeters, double width, double height) =>
    [
        Point(eastMeters, northMeters), Point(eastMeters + width, northMeters),
        Point(eastMeters + width, northMeters + height), Point(eastMeters, northMeters + height),
    ];

    private static GeoPoint Point(double eastMeters, double northMeters) =>
        new(Lat + (northMeters / MetersPerDegreeLatitude), Lon + (eastMeters / MetersPerDegreeLongitude));

    [Fact]
    public void Validate_ShouldAcceptASimpleRing() =>
        _geometry.Validate(Rectangle(0, 0, 100, 100)).Should().Be(RingValidationResult.Ok);

    [Fact]
    public void Validate_ShouldAcceptARingThatRepeatsItsFirstCornerAtTheEnd()
    {
        var ring = Rectangle(0, 0, 100, 100).Append(Point(0, 0)).ToList();
        _geometry.Validate(ring).Should().Be(RingValidationResult.Ok);
    }

    [Fact]
    public void Validate_ShouldRejectABowTieAsSelfCrossing()
    {
        IReadOnlyList<GeoPoint> bowTie = [Point(0, 0), Point(100, 100), Point(100, 0), Point(0, 100)];
        _geometry.Validate(bowTie).Should().Be(RingValidationResult.SelfCrossing);
    }

    [Fact]
    public void Validate_ShouldRejectCollinearCornersAsDegenerate()
    {
        IReadOnlyList<GeoPoint> line = [Point(0, 0), Point(50, 0), Point(100, 0)];
        _geometry.Validate(line).Should().Be(RingValidationResult.Degenerate);
    }

    [Fact]
    public void Validate_ShouldRejectARingBelowOneSquareMetreAsDegenerate() =>
        _geometry.Validate(Rectangle(0, 0, 0.5, 0.5)).Should().Be(RingValidationResult.Degenerate);

    [Fact]
    public void Validate_ShouldRejectTwoCornersUnderFiveCentimetresApart()
    {
        IReadOnlyList<GeoPoint> ring = [Point(0, 0), Point(100, 0), Point(100, 100), Point(100.02, 100), Point(0, 100)];
        _geometry.Validate(ring).Should().Be(RingValidationResult.DuplicateCorner);
    }

    [Fact]
    public void Validate_ShouldRejectFewerThanThreeCorners() =>
        _geometry.Validate([Point(0, 0), Point(10, 10)]).Should().Be(RingValidationResult.Degenerate);

    [Fact]
    public void UnionArea_ShouldCountOverlapOnce()
    {
        // Two 100 x 100 m squares overlapping by half: 10,000 + 10,000 - 5,000.
        var area = _geometry.UnionArea([Rectangle(0, 0, 100, 100), Rectangle(50, 0, 100, 100)]);

        area.Should().BeApproximately(15_000, 15_000 * 0.005);
    }

    [Fact]
    public void UnionArea_ShouldSumSeparateRings()
    {
        var area = _geometry.UnionArea([Rectangle(0, 0, 100, 100), Rectangle(500, 0, 50, 50)]);

        area.Should().BeApproximately(12_500, 12_500 * 0.005);
    }

    [Fact]
    public void Intersects_ShouldBeTrueWithinTheGrowth_AndFalseBeyondIt()
    {
        IReadOnlyList<IReadOnlyList<GeoPoint>> found = [Rectangle(0, 0, 100, 100)];

        // 20 m east of the found ring's edge.
        _geometry.Intersects([Rectangle(120, 0, 50, 50)], found, growMeters: 25).Should().BeTrue();

        // 40 m east of the found ring's edge.
        _geometry.Intersects([Rectangle(140, 0, 50, 50)], found, growMeters: 25).Should().BeFalse();
    }

    [Fact]
    public void Intersects_ShouldBeTrueForOverlappingRings() =>
        _geometry.Intersects([Rectangle(50, 50, 100, 100)], [Rectangle(0, 0, 100, 100)], growMeters: 0).Should().BeTrue();

    // ---- Join / Cut (specs/079 US4) ---------------------------------------

    /// <summary>The smallest distance from a point to any of the ring's corners, in metres.</summary>
    private static double NearestCornerMeters(IReadOnlyList<GeoPoint> ring, GeoPoint point) =>
        ring.Min(c => Math.Sqrt(
            Math.Pow((c.Latitude - point.Latitude) * MetersPerDegreeLatitude, 2) +
            Math.Pow((c.Longitude - point.Longitude) * MetersPerDegreeLongitude, 2)));

    [Fact]
    public void Join_ShouldGiveOneRing_AndKeepEveryCornerFarFromTheSeamWhereItWas()
    {
        // A 100 x 100 m ring, edited so one corner is at an odd spot; a footprint 1 m off its east side.
        var edited = new List<GeoPoint> { Point(0, 0), Point(100, 0), Point(100, 100), Point(0, 100), Point(-7.3, 52.1) };
        var footprint = Rectangle(101, 20, 40, 40);

        var joined = _geometry.Join(edited, footprint);

        _geometry.Validate(joined).Should().Be(RingValidationResult.Ok);
        NearestCornerMeters(joined, Point(-7.3, 52.1)).Should().BeLessThan(1e-3, "a hand-placed corner far from the seam is untouched");
        NearestCornerMeters(joined, Point(0, 0)).Should().BeLessThan(1e-3);
        NearestCornerMeters(joined, Point(141, 60)).Should().BeLessThan(1e-3, "the footprint's far corners come along");
        _geometry.UnionArea([joined]).Should().BeGreaterThan(_geometry.UnionArea([edited]) + 1_600 - 1);
    }

    [Fact]
    public void Cut_ShouldRestoreTheOriginalCornersAwayFromTheSeam_AndLeaveNoSliver()
    {
        var edited = new List<GeoPoint> { Point(0, 0), Point(100, 0), Point(100, 100), Point(0, 100), Point(-7.3, 52.1) };
        var footprint = Rectangle(101, 20, 40, 40);
        var joined = _geometry.Join(edited, footprint);

        var cut = _geometry.Cut(joined, footprint);

        _geometry.Validate(cut).Should().Be(RingValidationResult.Ok);
        NearestCornerMeters(cut, Point(-7.3, 52.1)).Should().BeLessThan(1e-3);
        NearestCornerMeters(cut, Point(0, 0)).Should().BeLessThan(1e-3);
        cut.Any(c => NearestCornerMeters(new List<GeoPoint> { c }, Point(141, 60)) < 1).Should().BeFalse("the footprint is gone");
        _geometry.UnionArea([cut]).Should().BeApproximately(_geometry.UnionArea([edited]), 120, "only the seam ground differs");
    }
}
