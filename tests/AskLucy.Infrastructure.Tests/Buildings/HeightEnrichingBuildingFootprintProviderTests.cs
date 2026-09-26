using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Buildings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/075 — <see cref="HeightEnrichingBuildingFootprintProvider"/>: which footprints take a
/// measured height, which never do, and that a height-source failure never fails the search.
/// </summary>
public sealed class HeightEnrichingBuildingFootprintProviderTests
{
    private static readonly GeoPoint Center = new(25.1560, 55.2218);

    private static BuildingFootprint Square(string id, double lat, double lon, BuildingHeightProvenance provenance = BuildingHeightProvenance.Assumed, double height = 9) =>
        new(id,
            [new(lat, lon), new(lat, lon + 0.0002), new(lat + 0.0002, lon + 0.0002), new(lat + 0.0002, lon), new(lat, lon)],
            height, provenance, string.Empty, IsSiteBuilding: false);

    private static BuildingFootprintResult Result(params BuildingFootprint[] buildings) =>
        new(buildings, Limited: false, ExcludedCount: 0, RadiusMetres: 200, BuildingFootprintSource.Rendered);

    private static (HeightEnrichingBuildingFootprintProvider Provider, IBuildingFootprintProvider Footprints, IBuildingHeightSource Heights) Create()
    {
        var footprints = Substitute.For<IBuildingFootprintProvider>();
        var heights = Substitute.For<IBuildingHeightSource>();
        return (new HeightEnrichingBuildingFootprintProvider(footprints, heights, NullLogger<HeightEnrichingBuildingFootprintProvider>.Instance), footprints, heights);
    }

    [Fact]
    public async Task SearchAsync_ShouldReplaceAnAssumedHeight_WithTheTallestMeasurementInsideTheFootprint()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Result(
            Square("block", 25.1560, 55.2218),
            Square("empty", 25.1570, 55.2218)));
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new MeasuredBuildingHeight(new GeoPoint(25.15605, 55.22185), 7.5),
            new MeasuredBuildingHeight(new GeoPoint(25.15615, 55.22195), 14.2),
            new MeasuredBuildingHeight(new GeoPoint(25.1590, 55.2218), 60),
        ]);

        var result = await provider.SearchAsync(Center, 200);

        result.Buildings[0].Should().Match<BuildingFootprint>(b => b.HeightMetres == 14.2 && b.HeightProvenance == BuildingHeightProvenance.Known);
        result.Buildings[1].Should().Match<BuildingFootprint>(b => b.HeightMetres == 9 && b.HeightProvenance == BuildingHeightProvenance.Assumed,
            "no measurement falls inside it — the 60 m one belongs to a building that isn't in the result");
        result.Source.Should().Be(BuildingFootprintSource.Rendered, "enrichment never changes where the footprints came from");
    }

    [Fact]
    public async Task SearchAsync_ShouldNeverOverwriteAKnownHeight()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Result(
            Square("tagged", 25.1560, 55.2218, BuildingHeightProvenance.Known, 828)));
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            [new MeasuredBuildingHeight(new GeoPoint(25.15605, 55.22185), 317.4)]);

        var result = await provider.SearchAsync(Center, 200);

        result.Buildings.Single().HeightMetres.Should().Be(828);
    }

    [Fact]
    public async Task SearchAsync_ShouldSearchHeightsBeyondTheRadius_SoFootprintsKeptWholeStillMatch()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Result());

        await provider.SearchAsync(Center, 200);

        await heights.Received(1).SearchAsync(Center, Arg.Is<int>(r => r > 200), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnTheFootprintsUnchanged_WhenTheHeightSourceFails()
    {
        var (provider, footprints, heights) = Create();
        var original = Result(Square("block", 25.1560, 55.2218));
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(original);
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<MeasuredBuildingHeight>>>(_ => throw new BuildingProviderUnavailableException("esri down"));

        var result = await provider.SearchAsync(Center, 200);

        result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task SearchAsync_ShouldRethrow_WhenTheFootprintsFail()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("every footprint source down"));
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var act = async () => await provider.SearchAsync(Center, 200);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>("the existing 503 mapping must still apply");
    }

    [Fact]
    public async Task SearchAsync_ShouldPropagateTheCallersCancellation()
    {
        var (provider, footprints, heights) = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        footprints.SearchAsync(Center, 200, cancellation.Token).Returns(Result());
        heights.SearchAsync(Center, Arg.Any<int>(), cancellation.Token)
            .Returns<Task<IReadOnlyList<MeasuredBuildingHeight>>>(_ => throw new OperationCanceledException(cancellation.Token));

        var act = async () => await provider.SearchAsync(Center, 200, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a cancelled request is not a height-source outage to log and hide");
    }
}
