using AskLucy.Application.Overpass;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.SiteAnalysis.Providers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist — the geometry data provider's one bounded query: parcels vs
/// streets, the road-class filter, the derived radius, and the unavailable outcome.
/// </summary>
public sealed class OverpassSiteGeometryDataProviderTests
{
    private static readonly GeoPoint Center = new(25.1560, 55.2210);

    private static readonly string[] ExpectedParcelValues = ["parcel", "cadastral"];

    private readonly IOverpassFeatureQueryProvider _queryProvider = Substitute.For<IOverpassFeatureQueryProvider>();
    private readonly OverpassSiteGeometryDataProvider _provider;

    public OverpassSiteGeometryDataProviderTests()
    {
        _provider = new OverpassSiteGeometryDataProvider(_queryProvider);
    }

    private static OverpassFeature Parcel(string id = "osm_way_1") => new(
        id,
        "way",
        new Dictionary<string, string> { ["boundary"] = "parcel" },
        Ring(55.2210, 25.1550, 0.001));

    private static OverpassFeature Street(string? name = "Test Street", string highway = "residential")
    {
        var tags = new Dictionary<string, string> { ["highway"] = highway };
        if (name is not null)
        {
            tags["name"] = name;
        }

        return new OverpassFeature(
            "osm_way_2",
            "way",
            tags,
            [new GeoPoint(25.1500, 55.2200), new GeoPoint(25.1520, 55.2200)]);
    }

    private static List<GeoPoint> Ring(double lon, double lat, double delta) =>
    [
        new(lat, lon),
        new(lat, lon + delta),
        new(lat + delta, lon + delta),
        new(lat + delta, lon),
        new(lat, lon),
    ];

    [Fact]
    public async Task ResolveAsync_ShouldSplitParcelsFromStreets_AndDropNonStreetClasses()
    {
        var footway = Street("Footpath", "footway");
        _queryProvider.QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>())
            .Returns([Parcel(), Street(), footway, Street(name: null)]);

        var outcome = await _provider.ResolveAsync(Center, null, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(DataResolutionOutcomeType.Confirmed);
        outcome.Value!.Parcels.Should().ContainSingle();
        outcome.Value.Streets.Should().HaveCount(2, "the footway is not a street; the unnamed road still is");
        outcome.Value.Streets.Should().Contain(street => street.Name == "Test Street");
        outcome.Value.Streets.Should().Contain(street => street.Name == null);
    }

    [Fact]
    public async Task ResolveAsync_ShouldAskForParcelsAndRoadClasses_InOneQuery()
    {
        OverpassFeatureQuery? captured = null;
        _queryProvider.QueryAsync(Arg.Do<OverpassFeatureQuery>(query => captured = query), Arg.Any<CancellationToken>())
            .Returns([]);

        await _provider.ResolveAsync(Center, null, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Filters.Should().HaveCount(2);
        captured.Filters[0].Key.Should().Be("boundary");
        captured.Filters[0].Values.Should().BeEquivalentTo(ExpectedParcelValues);
        captured.Filters[0].IncludeRelations.Should().BeTrue();
        captured.Filters[1].Key.Should().Be("highway");
        captured.Filters[1].Values.Should().Contain("residential").And.NotContain("footway");
        await _queryProvider.Received(1).QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenTheSourceCannotBeReached()
    {
        _queryProvider.QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<OverpassFeature>>(_ => throw new BoundaryProviderUnavailableException("overpass is down"));

        var outcome = await _provider.ResolveAsync(Center, null, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(DataResolutionOutcomeType.Unavailable);
        outcome.Reason.Should().Be("overpass is down");
        outcome.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldBeConfirmedButEmpty_WhenTheSourceHasNothing()
    {
        _queryProvider.QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>()).Returns([]);

        var outcome = await _provider.ResolveAsync(Center, null, TestContext.Current.CancellationToken);

        outcome.Type.Should().Be(DataResolutionOutcomeType.Confirmed);
        outcome.Value!.Parcels.Should().BeEmpty();
        outcome.Value.Streets.Should().BeEmpty();
    }

    [Fact]
    public void RadiusFor_ShouldUseThePointOnlyRadius_WhenThereIsNoBoundary()
    {
        OverpassSiteGeometryDataProvider.RadiusFor(null)
            .Should().Be(OverpassSiteGeometryDataProvider.PointOnlyQueryRadiusMeters);
    }

    [Fact]
    public void RadiusFor_ShouldGrowPastTheSiteEdge()
    {
        // ~100 m square: half diagonal ≈ 71 m, plus the margin.
        var ring = Ring(55.2210, 25.1550, 0.001);

        OverpassSiteGeometryDataProvider.RadiusFor(ring)
            .Should().BeGreaterThan(OverpassSiteGeometryDataProvider.MinimumQueryRadiusMeters);
    }

    [Fact]
    public void RadiusFor_ShouldBeCapped_ForADistrictSizedShape()
    {
        var ring = Ring(55.2210, 25.1550, 0.2); // ~22 km across

        OverpassSiteGeometryDataProvider.RadiusFor(ring)
            .Should().Be(OverpassSiteGeometryDataProvider.MaximumQueryRadiusMeters);
    }
}
