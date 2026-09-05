using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// <see cref="GeometryMath.BearingDegrees"/> — added to aim a Street View request from a
/// perimeter viewpoint back at the site, so the fetched frame actually faces the boundary rather
/// than pointing in whatever direction the panorama happened to be captured.
/// </summary>
public sealed class GeometryMathTests
{
    private const double Tolerance = 1.0;
    private static readonly GeoPoint Origin = new(25.1560, 55.2218);

    [Theory]
    [InlineData(0.0010, 0.0000, 0.0)]     // due north
    [InlineData(0.0000, 0.0010, 90.0)]    // due east
    [InlineData(-0.0010, 0.0000, 180.0)]  // due south
    [InlineData(0.0000, -0.0010, 270.0)]  // due west
    public void BearingDegrees_ShouldReportCompassBearing_TowardTheTarget(double deltaLat, double deltaLon, double expectedBearing)
    {
        var target = new GeoPoint(Origin.Latitude + deltaLat, Origin.Longitude + deltaLon);

        var bearing = GeometryMath.BearingDegrees(Origin, target);

        bearing.Should().BeApproximately(expectedBearing, Tolerance);
    }

    [Fact]
    public void BearingDegrees_ShouldStayWithinZeroTo360_ForADiagonalTarget()
    {
        // South-west of Origin: south is 180°, west is 270° — the exact angle between them
        // depends on longitude's latitude-scaled meters-per-degree, which isn't this test's
        // concern. What matters is the result wraps into a positive compass bearing rather than
        // atan2's native negative range.
        var target = new GeoPoint(Origin.Latitude - 0.0010, Origin.Longitude - 0.0010);

        var bearing = GeometryMath.BearingDegrees(Origin, target);

        bearing.Should().BeInRange(180.0, 270.0);
    }
}
