using AskLucy.Application.Buildings;
using AskLucy.Infrastructure.Buildings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using static AskLucy.Infrastructure.Tests.Buildings.FootprintTestGeometry;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/075, reworked by specs/076 — <see cref="HeightEnrichingBuildingFootprintProvider"/>: which
/// footprints take a measured height, when a taller part becomes a building of its own, when the
/// height map adds a building no footprint source had, and that a height-source failure never fails
/// the search. Layouts are in metres east/north of the site point.
/// </summary>
public sealed class HeightEnrichingBuildingFootprintProviderTests
{
    private const float Unmeasured = float.NaN;

    private static BuildingFootprintResult Result(params BuildingFootprint[] buildings) =>
        new(buildings, Limited: false, ExcludedCount: 0, RadiusMetres: 200, BuildingFootprintSource.Rendered);

    private static (HeightEnrichingBuildingFootprintProvider Provider, IBuildingFootprintProvider Footprints, IBuildingHeightSource Heights) Create(int maxBuildingCount = 300)
    {
        var footprints = Substitute.For<IBuildingFootprintProvider>();
        var heights = Substitute.For<IBuildingHeightSource>();
        var provider = new HeightEnrichingBuildingFootprintProvider(
            footprints, heights, Options.Create(new BuildingRetrievalOptions { MaxBuildingCount = maxBuildingCount }),
            NullLogger<HeightEnrichingBuildingFootprintProvider>.Instance);
        return (provider, footprints, heights);
    }

    private static async Task<BuildingFootprintResult> EnrichAsync(BuildingHeightMap map, int maxBuildingCount = 300, params BuildingFootprint[] buildings)
    {
        var (provider, footprints, heights) = Create(maxBuildingCount);
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Result(buildings));
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(map);
        return await provider.SearchAsync(Center, 200);
    }

    [Fact]
    public async Task SearchAsync_ShouldGiveAMallItsOwnHeight_AndItsTowerATallerBuildingOfItsOwn()
    {
        // BurJuman: one Esri feature, a 25 m mall around a 105 m tower.
        var map = Map((e, n) => Inside(e, n, -10, -10, 10, 10) ? 105 : Inside(e, n, -50, -50, 50, 50) ? 25 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("mall", -50, -50, 50, 50, site: true));

        result.Buildings.Should().HaveCount(2);
        result.Buildings[0].Should().Match<BuildingFootprint>(b =>
            b.Id == "mall" && b.HeightMetres == 25 && b.HeightProvenance == BuildingHeightProvenance.Known && b.IsSiteBuilding,
            "the median of a mall with a tower is the mall");
        result.Buildings[1].Should().Match<BuildingFootprint>(b =>
            b.Id == "mall_part0" && b.HeightMetres == 105 && b.HeightProvenance == BuildingHeightProvenance.Known && !b.IsSiteBuilding);
        result.Source.Should().Be(BuildingFootprintSource.Rendered, "enrichment never changes where the footprints came from");
    }

    [Fact]
    public async Task SearchAsync_ShouldFollowATowersSetbacks()
    {
        var map = Map((e, n) =>
            Inside(e, n, -5, -5, 5, 5) ? 100 : Inside(e, n, -15, -15, 15, 15) ? 60 : Inside(e, n, -50, -50, 50, 50) ? 25 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("mall", -50, -50, 50, 50));

        result.Buildings.Select(b => (b.Id, b.HeightMetres)).Should().Equal(
            ("mall", 25), ("mall_part0", 60), ("mall_part0_part0", 100));
    }

    [Fact]
    public async Task SearchAsync_ShouldKeepAnAssumedHeight_WhenTooLittleOfTheFootprintIsMeasured()
    {
        var map = Map((e, n) => Inside(e, n, 0, 0, 10, 40) ? 12 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("block", 0, 0, 40, 40));

        result.Buildings.Should().ContainSingle().Which.Should().Match<BuildingFootprint>(b =>
            b.HeightMetres == AssumedBuildingHeight.DefaultMetres && b.HeightProvenance == BuildingHeightProvenance.Assumed,
            "a quarter measured could be a neighbour's roof, not this building's");
    }

    [Fact]
    public async Task SearchAsync_ShouldNeverOverwriteAKnownHeight()
    {
        var map = Map((e, n) => Inside(e, n, 0, 0, 20, 20) ? 317.4f : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("tagged", 0, 0, 20, 20, 828, BuildingHeightProvenance.Known));

        result.Buildings.Should().ContainSingle().Which.HeightMetres.Should().Be(828);
    }

    [Fact]
    public async Task SearchAsync_ShouldNotAddAGhostBuilding_BesideAFootprintThatIsAFewMetresOff()
    {
        // The measured roof sits 4 m east of the footprint, as the sources commonly disagree.
        var map = Map((e, n) => Inside(e, n, 4, 0, 24, 20) ? 12 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("villa", 0, 0, 20, 20));

        result.Buildings.Should().ContainSingle().Which.HeightMetres.Should().Be(12);
    }

    [Fact]
    public async Task SearchAsync_ShouldAddMeasuredBuildings_ThatNoFootprintSourceHad_OnlyWithinTheRadius()
    {
        var map = Map((e, n) => Inside(e, n, 60, 60, 80, 80) ? 12 : Inside(e, n, 250, 250, 270, 270) ? 30 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("known", -50, -50, -30, -30));

        result.Buildings.Should().HaveCount(2);
        result.Buildings[1].Should().Match<BuildingFootprint>(b =>
            b.Id == "esri_0" && b.HeightMetres == 12 && b.HeightProvenance == BuildingHeightProvenance.Known && !b.IsSiteBuilding);
    }

    [Fact]
    public async Task SearchAsync_ShouldMakeAMeasuredBuildingTheSiteBuilding_WhenNoFootprintSourceFoundOne()
    {
        var map = Map((e, n) => Inside(e, n, -10, -10, 10, 10) ? 15 : Unmeasured);

        var result = await EnrichAsync(map, buildings: Rect("elsewhere", 100, 100, 120, 120));

        result.Buildings.Should().ContainSingle(b => b.IsSiteBuilding).Which.Id.Should().Be("esri_0");
    }

    [Fact]
    public async Task SearchAsync_ShouldStopAtTheBuildingLimit_AndSaySo()
    {
        var map = Map((e, n) => Inside(e, n, 60, 60, 80, 80) ? 12 : Unmeasured);

        var result = await EnrichAsync(map, maxBuildingCount: 1, Rect("known", -50, -50, -30, -30));

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("known");
        result.Limited.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ShouldSearchHeightsBeyondTheRadius_SoFootprintsKeptWholeStillMatch()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Result());
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BuildingHeightMap.Empty);

        await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        await heights.Received(1).SearchAsync(Center, Arg.Is<int>(r => r > 200), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnTheFootprintsUnchanged_WhenTheHeightSourceFails()
    {
        var (provider, footprints, heights) = Create();
        var original = Result(Rect("block", 0, 0, 20, 20));
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(original);
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<BuildingHeightMap>>(_ => throw new BuildingProviderUnavailableException("esri down"));

        var result = await provider.SearchAsync(Center, 200, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task SearchAsync_ShouldRethrow_WhenTheFootprintsFail()
    {
        var (provider, footprints, heights) = Create();
        footprints.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("every footprint source down"));
        heights.SearchAsync(Center, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BuildingHeightMap.Empty);

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
            .Returns<Task<BuildingHeightMap>>(_ => throw new OperationCanceledException(cancellation.Token));

        var act = async () => await provider.SearchAsync(Center, 200, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a cancelled request is not a height-source outage to log and hide");
    }
}
