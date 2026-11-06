using AskLucy.Application.Locations;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Specialists;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Spatial;

/// <summary>specs/094 — the POI specialist retrieves; it never asks, draws or narrates.</summary>
public sealed class PoiDiscoverySpecialistTests
{
    private readonly IPoiQueryProvider _provider = Substitute.For<IPoiQueryProvider>();
    private readonly PoiCategoryCatalog _catalog = new();
    private readonly PoiDiscoverySpecialist _specialist;

    public PoiDiscoverySpecialistTests()
    {
        _specialist = new PoiDiscoverySpecialist(_provider, _catalog);
    }

    private static SpatialOperation Operation(string category = "schools") =>
        new("op-1", SpatialOperationKinds.PoiDiscovery, [new FeatureSelector(Category: category)]);

    private static SpatialExecutionContext Context() =>
        new(
            "user-1",
            Guid.NewGuid(),
            new ResolvedStudyArea(
                "Muscat",
                new GeoPoint(23.588, 58.382),
                10_000,
                Viewport: new ViewportBounds(23.65, 58.45, 23.52, 58.31)),
            MapContext: null,
            []);

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAMarkerDraftWithCatalogStyling()
    {
        _provider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>())
            .Returns([new PoiFeature("osm_1", "School A", 23.5, 58.3, new Dictionary<string, string>())]);

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Succeeded);
        var layer = outcome.Layers.Should().ContainSingle().Subject;
        layer.Kind.Should().Be(SpatialLayerKind.Markers);
        layer.LegendLabel.Should().Be("Schools");
        layer.Style!.Color.Should().Be("#4285F4");
        outcome.Coverage.Should().ContainSingle(coverage => coverage.SourceId == PoiDiscoverySpecialist.SourceId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithTheSupportedCategoriesForAnUnknownOne()
    {
        var outcome = await _specialist.ExecuteAsync(Operation("spaceports"), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
        outcome.FailureReason.Should().Contain("school").And.Contain("hospital");
        await _provider.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
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
    public async Task ExecuteAsync_ShouldReportNoMatchesAsEmpty()
    {
        _provider.QueryAsync(Arg.Any<PoiQuery>(), Arg.Any<CancellationToken>()).Returns([]);

        var outcome = await _specialist.ExecuteAsync(Operation(), Context(), default);

        outcome.Status.Should().Be(SpatialOperationStatus.Empty);
        outcome.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWhenNoStudyAreaWasResolved()
    {
        var outcome = await _specialist.ExecuteAsync(
            Operation(), Context() with { StudyArea = null }, default);

        outcome.Status.Should().Be(SpatialOperationStatus.Failed);
    }
}
