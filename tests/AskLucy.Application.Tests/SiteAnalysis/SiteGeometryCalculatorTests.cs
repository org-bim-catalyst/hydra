using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist data-model.md § 4 — the pure measurement rules: area,
/// principal-axis dimensions and orientation, frontage attribution and tolerance, the fallback-buffer
/// recognition, and parcel selection by containment.
/// </summary>
public sealed class SiteGeometryCalculatorTests
{
    private static readonly GeoPoint Centre = new(25.0900, 55.2000);

    private const double MetresPerDegreeLatitude = 111_320.0;

    private static readonly SiteGeometryCalculator Calculator = new();

    private static GeoPoint Offset(double east, double north)
    {
        var latitude = Centre.Latitude + (north / MetresPerDegreeLatitude);
        var longitude = Centre.Longitude + (east / (MetresPerDegreeLatitude * Math.Cos(Centre.Latitude * Math.PI / 180.0)));
        return new GeoPoint(latitude, longitude);
    }

    /// <summary>A closed rectangle, long axis rotated clockwise from east by <paramref name="rotationDegrees"/>.</summary>
    private static List<GeoPoint> Rectangle(
        double lengthMeters, double widthMeters, double rotationDegrees = 0.0, GeoPoint? centre = null)
    {
        var reference = centre ?? Centre;
        var angle = rotationDegrees * Math.PI / 180.0;
        var halfLength = lengthMeters / 2.0;
        var halfWidth = widthMeters / 2.0;

        (double X, double Y)[] corners =
        [
            (-halfLength, -halfWidth),
            (halfLength, -halfWidth),
            (halfLength, halfWidth),
            (-halfLength, halfWidth),
        ];

        var ring = corners
            .Select(corner => (
                X: (corner.X * Math.Cos(angle)) - (corner.Y * Math.Sin(angle)),
                Y: (corner.X * Math.Sin(angle)) + (corner.Y * Math.Cos(angle))))
            .Select(local =>
            {
                var latitude = reference.Latitude + (local.Y / MetresPerDegreeLatitude);
                var longitude = reference.Longitude + (local.X / (MetresPerDegreeLatitude * Math.Cos(reference.Latitude * Math.PI / 180.0)));
                return new GeoPoint(latitude, longitude);
            })
            .ToList();

        ring.Add(ring[0]);
        return ring;
    }

    private static SiteGeometryStreet Street(
        string? name, params (double East, double North)[] points) =>
        new("osm_way_street", name, [.. points.Select(point => Offset(point.East, point.North))]);

    [Fact]
    public void Measure_ShouldComputeArea()
    {
        var measurements = Calculator.Measure(Rectangle(100, 60), []);

        measurements.AreaSquareMeters.Should().BeApproximately(6_000, 60);
    }

    [Fact]
    public void Measure_ShouldReportTheLargerExtentFirst()
    {
        var measurements = Calculator.Measure(Rectangle(60, 100), []);

        measurements.LengthMeters.Should().BeApproximately(100, 2);
        measurements.WidthMeters.Should().BeApproximately(60, 2);
    }

    [Fact]
    public void Measure_ShouldRecoverTheDimensionsOfARotatedSite()
    {
        var measurements = Calculator.Measure(Rectangle(100, 60, rotationDegrees: 30), []);

        measurements.LengthMeters.Should().BeApproximately(100, 2);
        measurements.WidthMeters.Should().BeApproximately(60, 2);
    }

    [Fact]
    public void Measure_ShouldReportTheLongAxisBearing()
    {
        Calculator.Measure(Rectangle(100, 60), []).OrientationBearingDegrees
            .Should().BeApproximately(90, 1, "the long axis runs east–west");

        Calculator.Measure(Rectangle(100, 60, rotationDegrees: 30), []).OrientationBearingDegrees
            .Should().BeApproximately(60, 1);
    }

    [Fact]
    public void Measure_ShouldReportNoOrientation_ForANearSquare()
    {
        var measurements = Calculator.Measure(Rectangle(100, 98), []);

        measurements.OrientationBearingDegrees.Should().BeNull(
            "two nearly equal axes have no defensible principal direction");
        measurements.LengthMeters.Should().BeApproximately(100, 2);
    }

    [Fact]
    public void Measure_ShouldAttributeFrontageToTheNearestNamedStreet()
    {
        var measurements = Calculator.Measure(
            Rectangle(100, 60),
            [Street("Test Street", (-60, -50), (60, -50))]);

        measurements.Frontages.Should().ContainSingle();
        measurements.Frontages[0].StreetName.Should().Be("Test Street");
        measurements.Frontages[0].LengthMeters.Should().BeApproximately(100, 2);
        measurements.TotalFrontageMeters.Should().BeApproximately(100, 2);
    }

    [Fact]
    public void Measure_ShouldNotCountAnUnnamedStreet()
    {
        var measurements = Calculator.Measure(
            Rectangle(100, 60),
            [Street(name: null, (-60, -50), (60, -50))]);

        measurements.Frontages.Should().BeEmpty("frontage must be attributable to a street the reader can name");
    }

    [Fact]
    public void Measure_ShouldNotCountAStreetBeyondTheTolerance()
    {
        var measurements = Calculator.Measure(
            Rectangle(100, 60),
            [Street("Far Street", (-60, -70), (60, -70))]);

        measurements.Frontages.Should().BeEmpty();
    }

    [Fact]
    public void Measure_ShouldReportTwoStreetsSeparately_WithATotal()
    {
        var measurements = Calculator.Measure(
            Rectangle(100, 60),
            [
                Street("South Street", (-60, -50), (60, -50)),
                Street("North Street", (-60, 50), (60, 50)),
            ]);

        measurements.Frontages.Should().HaveCount(2);
        measurements.TotalFrontageMeters.Should().BeApproximately(200, 4);
    }

    [Fact]
    public void Measure_ShouldRecogniseThePlatformsFallbackBuffer()
    {
        var buffer = GeometryMath.CirclePolygon(Centre, 100.0);

        Calculator.Measure(buffer, []).LooksLikeResolvedBuffer.Should().BeTrue();
        Calculator.Measure(Rectangle(100, 60), []).LooksLikeResolvedBuffer.Should().BeFalse();
    }

    [Fact]
    public void FindParcel_ShouldReturnTheSmallestContainingParcel()
    {
        var large = new SiteGeometryParcel("large", Rectangle(400, 400), new Dictionary<string, string>());
        var small = new SiteGeometryParcel("small", Rectangle(80, 80), new Dictionary<string, string>());
        var elsewhere = new SiteGeometryParcel("elsewhere", Rectangle(50, 50, centre: new GeoPoint(25.2000, 55.3000)), new Dictionary<string, string>());

        var parcel = Calculator.FindParcel([large, small, elsewhere], Centre);

        parcel.Should().NotBeNull();
        parcel!.Id.Should().Be("small");
    }

    [Fact]
    public void FindParcel_ShouldReturnNull_WhenNoParcelContainsTheSite()
    {
        var elsewhere = new SiteGeometryParcel(
            "elsewhere", Rectangle(50, 50, centre: new GeoPoint(25.2000, 55.3000)), new Dictionary<string, string>());

        Calculator.FindParcel([elsewhere], Centre).Should().BeNull();
    }
}
