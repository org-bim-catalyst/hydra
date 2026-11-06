using AskLucy.Application.Locations;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>
/// specs/093 — within is inclusive, beyond is exclusive, adjacent bands never both claim a
/// feature on their shared boundary, and a provider outage is never reported as an empty result.
/// </summary>
public sealed class ProximityAnalysisSpecialistTests
{
    private readonly IPoiQueryProvider _provider = Substitute.For<IPoiQueryProvider>();
    private readonly PoiCategoryCatalog _catalog = new();
    private readonly ProximityAnalysisSpecialist _specialist;

    public ProximityAnalysisSpecialistTests()
    {
        _specialist = new ProximityAnalysisSpecialist(_provider, _catalog);
    }

    // 0° longitude offset ~1.11 km per 0.01° of latitude at this latitude.
    private static readonly PoiFeature Hospital = new("osm_h1", "Central Hospital", 23.5, 58.3, Tags());
    private static readonly PoiFeature SchoolNear = new("osm_s1", "Near School", 23.505, 58.3, Tags());   // ~556 m
    private static readonly PoiFeature SchoolFar = new("osm_s2", "Far School", 23.53, 58.3, Tags());      // ~3.3 km

    private static Dictionary<string, string> Tags() => new();

    private static SpatialExecutionContext Context() =>
        new(
            "user-1",
            Guid.NewGuid(),
            new ResolvedStudyArea(
                "Muscat",
                new GeoPoint(23.5, 58.3),
                10_000,
                Viewport: new ViewportBounds(23.6, 58.4, 23.4, 58.2)),
            MapContext: null,
            []);

    private static SpatialOperation Operation(
        string source = "schools",
        string? target = "hospitals",
        double? distance = 1,
        string unit = "km",
        bool beyond = false,
        params SpatialDistanceThreshold[] bands)
    {
        var targets = new List<FeatureSelector> { new(Category: source, Role: "source") };
        if (target is not null)
        {
            targets.Add(new FeatureSelector(Category: target, Role: "target"));
        }

        var thresholds = bands.Length > 0
            ? bands
            : distance is { } value ? [new SpatialDistanceThreshold(0, value)] : null;

        var parameters = thresholds is null
            ? null
            : new SpatialOperationParameters(thresholds, unit, beyond ? "beyond" : "within");

        return new SpatialOperation("op-1", SpatialOperationKinds.ProximityAnalysis, targets, parameters);
    }

    private void GivenFeatures(params PoiFeature[] schools) =>
        _provider.QueryAsync(Arg.Is<PoiQuery>(q => q.Category.Key == "school"), Arg.Any<CancellationToken>())
            .Returns(schools);

    private void GivenHospitals(params PoiFeature[] hospitals) =>
        _provider.QueryAsync(Arg.Is<PoiQuery>(q => q.Category.Key == "hospital"), Arg.Any<CancellationToken>())
            .Returns(hospitals);

    [Fact]
    public async Task ExecuteAsync_ShouldReturnOnlyFeaturesWithinTheThreshold()
    {
        GivenFeatures(SchoolNear, SchoolFar);
        GivenHospitals(Hospital);

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Succeeded);
        var markers = outcome.Layers.Single(layer => layer.Kind == SpatialLayerKind.Markers);
        markers.Markers!.Select(marker => marker.Id).Should().Equal("osm_s1");
        // The buffer makes the rule visible and uses the target's own colour.
        outcome.Layers.Should().Contain(layer => layer.Kind == SpatialLayerKind.Buffer);
        outcome.Layers.Single(layer => layer.Kind == SpatialLayerKind.Buffer).Style!.Color
            .Should().Be(_catalog.FindByKey("hospital")!.Color);
    }

    [Fact]
    public async Task ExecuteAsync_Beyond_ShouldExcludeAnythingAtOrInsideTheThreshold()
    {
        GivenFeatures(SchoolNear, SchoolFar);
        GivenHospitals(Hospital);

        var outcome = await _specialist.ExecuteAsync(Operation(beyond: true), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Succeeded);
        var markers = outcome.Layers.Single(layer => layer.Kind == SpatialLayerKind.Markers);
        markers.Markers!.Select(marker => marker.Id).Should().Equal("osm_s2");
        outcome.Layers.Should().NotContain(layer => layer.Kind == SpatialLayerKind.Buffer);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAssignAdjacentBandsWithoutDoubleCounting()
    {
        // 0–500 m and 500 m–1.5 km. SchoolNear is ~556 m: band two only.
        GivenFeatures(
            new PoiFeature("osm_zero", "On The Hospital", 23.5, 58.3, Tags()),
            SchoolNear,
            SchoolFar);
        GivenHospitals(Hospital);

        var outcome = await _specialist.ExecuteAsync(
            Operation(distance: null, bands:
            [
                new SpatialDistanceThreshold(0, 0.5),
                new SpatialDistanceThreshold(0.5, 1.5),
            ]),
            Context(),
            default);

        outcome.Status.Should().Be(SpatialOperationStatus.Succeeded);
        var layers = outcome.Layers.Where(layer => layer.Kind == SpatialLayerKind.Markers).ToList();
        layers.Should().HaveCount(2);
        layers[0].Markers!.Select(marker => marker.Id).Should().Equal("osm_zero");
        layers[1].Markers!.Select(marker => marker.Id).Should().Equal("osm_s1");
        // SchoolFar is beyond the final upper bound and belongs to no band.
        layers.SelectMany(layer => layer.Markers!).Select(marker => marker.Id).Should().NotContain("osm_s2");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithTheSupportedCategoriesForAnUnknownOne()
    {
        var outcome = await _specialist.ExecuteAsync(Operation(source: "spaceports"), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
        outcome.FailureReason.Should().Contain("school").And.Contain("hospital");
        await _provider.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWhenTheRequestNamesNoTarget()
    {
        var outcome = await _specialist.ExecuteAsync(Operation(target: null), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
        outcome.FailureReason.Should().Contain("measure the distance");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWhenNoStudyAreaWasResolved()
    {
        var context = Context() with { StudyArea = null };

        var outcome = await _specialist.ExecuteAsync(Operation(), context, default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
        outcome.FailureReason.Should().Contain("which area");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportAnOutageAsFailure_NotAsEmpty()
    {
        _provider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PoiFeature>>(_ => throw new BoundaryProviderUnavailableException("down"));

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
        outcome.FailureReason.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportNoSourcesAsEmpty()
    {
        GivenFeatures();
        GivenHospitals(Hospital);

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Empty);
        outcome.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportNoTargetsAsEmptyWithAReason()
    {
        GivenFeatures(SchoolNear);
        GivenHospitals();

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Empty);
        outcome.Warnings.Should().ContainSingle(warning => warning.Contains("hospitals"));
    }
}
