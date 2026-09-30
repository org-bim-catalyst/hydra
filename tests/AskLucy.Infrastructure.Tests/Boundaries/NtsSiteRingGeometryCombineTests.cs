using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>specs/079 - adding a circle to an outline and cutting one out of it (union and subtraction).</summary>
public sealed class NtsSiteRingGeometryCombineTests
{
    private const double Lat = 23.59;
    private const double Lon = 58.40;
    private const double MetersPerDegreeLatitude = 111_320.0;

    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(Lat * Math.PI / 180);

    private readonly NtsSiteRingGeometry _geometry = new();

    private static GeoPoint At(double east, double north) =>
        new(Lat + (north / MetersPerDegreeLatitude), Lon + (east / MetersPerDegreeLongitude));

    private static IReadOnlyList<GeoPoint> Rectangle(double east, double north, double width, double height) =>
        [At(east, north), At(east + width, north), At(east + width, north + height), At(east, north + height)];

    private static IReadOnlyList<GeoPoint> Circle(double east, double north, double radius) =>
        GeometryMath.CirclePolygon(At(east, north), radius, 72);

    private static double Area(IReadOnlyList<GeoPoint> ring) => GeometryMath.AreaSquareMeters(ring);

    private static double TotalArea(CombineResult result) => result.Rings.Sum(r => Area(r));

    [Fact]
    public void Adding_ACircleThatOverlapsTheOutline_MergesIntoOneRing()
    {
        // 100 x 100 m square; a 40 m circle centred on its right edge adds about half the circle.
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(100, 50, 40), CombineOperation.Add);

        result.Failure.Should().Be(CombineFailure.None);
        result.Rings.Should().HaveCount(1);
        TotalArea(result).Should().BeApproximately(10_000 + (Math.PI * 40 * 40 / 2), 150);
    }

    [Fact]
    public void Adding_ACircleThatDoesNotTouch_IsARingOfItsOwn_AfterTheOutline()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(300, 50, 30), CombineOperation.Add);

        result.Failure.Should().Be(CombineFailure.None);
        result.Rings.Should().HaveCount(2);
        Area(result.Rings[0]).Should().BeApproximately(10_000, 50);
        Area(result.Rings[1]).Should().BeApproximately(Math.PI * 30 * 30, 100);
    }

    [Fact]
    public void Adding_ACircleInsideTheOutline_ChangesNothing()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(50, 50, 20), CombineOperation.Add);

        result.Rings.Should().HaveCount(1);
        TotalArea(result).Should().BeApproximately(10_000, 50);
    }

    [Fact]
    public void Adding_MergesRingsThatTheCircleBridges()
    {
        var result = _geometry.Combine(
            [Rectangle(0, 0, 100, 100), Rectangle(130, 0, 100, 100)], Circle(115, 50, 30), CombineOperation.Add);

        result.Rings.Should().HaveCount(1);
    }

    [Fact]
    public void Cutting_ACircleAtTheEdge_TakesABiteOut()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(100, 50, 30), CombineOperation.Cut);

        result.Failure.Should().Be(CombineFailure.None);
        result.Rings.Should().HaveCount(1);
        TotalArea(result).Should().BeApproximately(10_000 - (Math.PI * 30 * 30 / 2), 150);
    }

    [Fact]
    public void Cutting_ACircleAcrossTheOutline_SplitsItIntoTwoRings()
    {
        // A long thin strip cut through the middle by a circle wider than the strip.
        var result = _geometry.Combine([Rectangle(0, 0, 200, 20)], Circle(100, 10, 30), CombineOperation.Cut);

        result.Failure.Should().Be(CombineFailure.None);
        result.Rings.Should().HaveCount(2);
    }

    [Fact]
    public void Cutting_ACircleWhollyInside_IsRefused_BecauseItWouldLeaveAHole()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(50, 50, 20), CombineOperation.Cut);

        result.Failure.Should().Be(CombineFailure.HoleNotSupported);
        result.Rings.Should().BeEmpty();
    }

    [Fact]
    public void Cutting_ACircleThatCoversEverything_LeavesNothing()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 40, 40)], Circle(20, 20, 100), CombineOperation.Cut);

        result.Failure.Should().Be(CombineFailure.NothingLeft);
    }

    [Fact]
    public void Cutting_ACircleThatDoesNotTouch_ChangesNothing()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(400, 50, 30), CombineOperation.Cut);

        result.Rings.Should().HaveCount(1);
        TotalArea(result).Should().BeApproximately(10_000, 50);
    }

    [Fact]
    public void TheRingHoldingTheFirstInputRing_StaysFirst_EvenWhenAnotherIsLarger()
    {
        // Ring 0 is the small one; an added circle far away is larger than it.
        var result = _geometry.Combine([Rectangle(0, 0, 40, 40)], Circle(400, 20, 100), CombineOperation.Add);

        result.Rings.Should().HaveCount(2);
        Area(result.Rings[0]).Should().BeApproximately(1_600, 30);
    }

    [Fact]
    public void Rings_AreReturnedOpen_WithoutARepeatedClosingCorner()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(100, 50, 30), CombineOperation.Add);

        foreach (var ring in result.Rings)
        {
            ring[0].Should().NotBe(ring[^1]);
            ring.Count.Should().BeGreaterThanOrEqualTo(3);
        }
    }

    [Fact]
    public void ResultRings_AreValidOutlines()
    {
        var result = _geometry.Combine([Rectangle(0, 0, 100, 100)], Circle(100, 50, 40), CombineOperation.Add);

        result.Rings.Should().OnlyContain(ring => _geometry.Validate(ring) == RingValidationResult.Ok);
    }

    [Fact]
    public void AcceptsInputRingsThatRepeatTheirFirstCorner()
    {
        var closed = Rectangle(0, 0, 100, 100).Append(At(0, 0)).ToList();

        var result = _geometry.Combine([closed], Circle(100, 50, 30), CombineOperation.Add);

        result.Failure.Should().Be(CombineFailure.None);
    }

    [Fact]
    public void NoRings_LeavesNothing() =>
        _geometry.Combine([], Circle(0, 0, 10), CombineOperation.Add).Failure.Should().Be(CombineFailure.NothingLeft);
}
