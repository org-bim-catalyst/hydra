using AskLucy.Application.Spatial;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>specs/094 — the bundle is the extension contract, so its version and shape are enforced.</summary>
public sealed class SpatialBundleValidatorTests
{
    private readonly SpatialBundleValidator _validator = new();

    private static SpatialRequestBundle Bundle(
        int version = SpatialRequestBundle.CurrentVersion,
        IReadOnlyList<SpatialOperation>? operations = null,
        MapViewportContext? mapContext = null) =>
        new(
            version,
            "bundle-1",
            DateTime.UtcNow,
            operations ?? [Operation("op-1", "poi.discovery")],
            new SpatialPreferences(),
            mapContext);

    private static SpatialOperation Operation(
        string id,
        string kind,
        IReadOnlyList<string>? dependsOn = null,
        StudyAreaReference? area = null) =>
        new(id, kind, [new FeatureSelector(Category: "school")], null, area, dependsOn);

    [Fact]
    public void Validate_ShouldRejectAnUnsupportedVersion()
    {
        var result = _validator.Validate(Bundle(version: 2));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("version"));
    }

    [Fact]
    public void Validate_ShouldRejectABundleWithNoOperations()
    {
        var result = _validator.Validate(Bundle(operations: []));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldRejectDuplicateOperationIds()
    {
        var result = _validator.Validate(Bundle(operations:
            [Operation("op-1", "poi.discovery"), Operation("op-1", "proximity.analysis")]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("unique"));
    }

    [Fact]
    public void Validate_ShouldRejectAForwardDependency()
    {
        var result = _validator.Validate(Bundle(operations:
            [Operation("op-1", "poi.discovery", dependsOn: ["op-2"]), Operation("op-2", "proximity.analysis")]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("earlier"));
    }

    [Fact]
    public void Validate_ShouldRejectNaNCoordinatesInMapContext()
    {
        var mapContext = new MapViewportContext(
            double.NaN, 58.38, 12, 23.52, 58.31, 23.65, 58.45);

        var result = _validator.Validate(Bundle(mapContext: mapContext));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("map view"));
    }

    [Fact]
    public void Validate_ShouldRejectInvertedBounds()
    {
        var mapContext = new MapViewportContext(
            23.588, 58.382, 12, 23.65, 58.45, 23.52, 58.31);

        _validator.Validate(Bundle(mapContext: mapContext)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldAcceptAValidBundle()
    {
        var mapContext = new MapViewportContext(
            23.588, 58.382, 12, 23.52, 58.31, 23.65, 58.45);

        _validator.Validate(Bundle(mapContext: mapContext)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldRejectAThresholdThatIsNotPositive()
    {
        var operation = new SpatialOperation(
            "op-1",
            SpatialOperationKinds.ProximityAnalysis,
            [new FeatureSelector(Category: "school", Role: "source")],
            new SpatialOperationParameters([new SpatialDistanceThreshold(0, -1)], "km", "within"),
            new StudyAreaReference(StudyAreaKind.ExplicitGeometry, Polygon:
                [new GeoPoint(23.5, 58.3), new GeoPoint(23.6, 58.3), new GeoPoint(23.6, 58.4)]));

        _validator.Validate(Bundle(operations: [operation])).IsValid.Should().BeFalse();
    }
}
