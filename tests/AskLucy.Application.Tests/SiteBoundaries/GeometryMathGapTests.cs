using AskLucy.Application.SiteBoundaries;
using FluentAssertions;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/077 — the distance between two footprints, which decides "connected" from "nearby".</summary>
public sealed class GeometryMathGapTests
{
    [Fact]
    public void GapMeters_IsZeroForASharedWall() =>
        GeometryMath.GapMeters(Mall, OfficeTower.Ring).Should().BeApproximately(0, 0.01);

    [Fact]
    public void GapMeters_MeasuresHalfAMetre() =>
        GeometryMath.GapMeters(Mall, ArjaanHotel.Ring).Should().BeApproximately(0.5, 0.05);

    [Fact]
    public void GapMeters_MeasuresAcrossTheStreet() =>
        GeometryMath.GapMeters(Mall, TowerAcrossTheStreet.Ring).Should().BeApproximately(26, 0.1);

    [Fact]
    public void GapMeters_IsZeroWhenOneRingHoldsTheOther() =>
        GeometryMath.GapMeters(Mall, Rect(40, 40, 60, 60)).Should().Be(0);

    [Fact]
    public void GapMeters_IsInfiniteForAnEmptyRing() =>
        GeometryMath.GapMeters(Mall, []).Should().Be(double.PositiveInfinity);

    [Fact]
    public void DistanceToRingMeters_IsZeroInsideAndTheWallDistanceOutside()
    {
        GeometryMath.DistanceToRingMeters(Point(50, 50), Mall).Should().Be(0);
        GeometryMath.DistanceToRingMeters(Point(-37, 50), Mall).Should().BeApproximately(37, 0.1);
    }
}
