using AskLucy.Application.Buildings;
using AskLucy.Infrastructure.Buildings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static AskLucy.Infrastructure.Tests.Buildings.FootprintTestGeometry;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/053-rendered-building-footprints, reworked by specs/076 —
/// <see cref="CompositeBuildingFootprintProvider"/>: which source's footprints are kept, how the
/// others fill its gaps and pass on heights, which building is the site's, and how failures and slow
/// sources resolve. Faked <see cref="IBuildingFootprintProvider"/>s only (no HTTP, no image
/// decoding — that belongs to each inner provider's own tests). Layouts are in metres east/north of
/// the site point.
/// </summary>
public sealed class CompositeBuildingFootprintProviderTests
{
    private static readonly BuildingFootprint SampleBuilding = Rect("sample", -40, -40, -20, -20);

    private static BuildingFootprintResult Found(BuildingFootprintSource source, params BuildingFootprint[] buildings) =>
        new(buildings.Length == 0 ? [SampleBuilding] : buildings, Limited: false, ExcludedCount: 0, RadiusMetres: 200, source);

    private static BuildingFootprintResult Empty(BuildingFootprintSource source) =>
        new([], Limited: false, ExcludedCount: 0, RadiusMetres: 200, source);

    private static CompositeBuildingFootprintProvider Composite(
        IReadOnlyList<IBuildingFootprintProvider> providers, TimeSpan? stragglerBudget = null, int maxBuildingCount = 300) =>
        new(providers,
            new BuildingConflationOptions { StragglerBudget = stragglerBudget ?? TimeSpan.FromSeconds(8) },
            new BuildingRetrievalOptions { MaxBuildingCount = maxBuildingCount },
            NullLogger<CompositeBuildingFootprintProvider>.Instance);

    private static (IBuildingFootprintProvider Primary, IBuildingFootprintProvider Fallback, CompositeBuildingFootprintProvider Composite) CreateComposite(
        TimeSpan? stragglerBudget = null, int maxBuildingCount = 300)
    {
        var primary = Substitute.For<IBuildingFootprintProvider>();
        var fallback = Substitute.For<IBuildingFootprintProvider>();
        return (primary, fallback, Composite([primary, fallback], stragglerBudget, maxBuildingCount));
    }

    private static (IBuildingFootprintProvider Rendered, IBuildingFootprintProvider Overture, IBuildingFootprintProvider Osm, CompositeBuildingFootprintProvider Composite) CreateChain()
    {
        var rendered = Substitute.For<IBuildingFootprintProvider>();
        var overture = Substitute.For<IBuildingFootprintProvider>();
        var osm = Substitute.For<IBuildingFootprintProvider>();
        return (rendered, overture, osm, Composite([rendered, overture, osm]));
    }

    private static void Returns(IBuildingFootprintProvider provider, BuildingFootprintResult result) =>
        provider.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(result);

    private static void Unavailable(IBuildingFootprintProvider provider) =>
        provider.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("down"));

    [Fact]
    public async Task SearchAsync_ShouldKeepThePrimarysFootprints_AndAskEverySourceAtOnce()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Empty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Rendered);
        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("sample");
        await fallback.Received(1).SearchAsync(Center, 200, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ShouldFallBack_WhenThePrimaryReturnsEmpty()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Empty(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Osm);
        result.Buildings.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldFallBack_WhenThePrimaryThrows_AndSurfaceNoError()
    {
        var (primary, fallback, composite) = CreateComposite();
        Unavailable(primary);
        Returns(fallback, Found(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Osm);
        result.Buildings.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldAddBuildingsThePrimaryMissed()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", 50, 50, 70, 70)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Select(b => b.Id).Should().Equal("sample", "osm_way_1");
        result.Source.Should().Be(BuildingFootprintSource.Rendered, "the footprints kept whole are still the primary's");
    }

    [Fact]
    public async Task SearchAsync_ShouldNeverDrawOneBuildingTwice_WhenTwoSourcesOutlineItSlightlyApart()
    {
        // specs/052 research D13 — two offset outlines would each cast their own shadow.
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", -34, -40, -14, -20)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("sample");
    }

    [Fact]
    public async Task SearchAsync_ShouldTakeARecordedHeight_FromAnotherSourcesOutlineOfTheSameBuilding()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", -42, -40, -22, -20, 40, BuildingHeightProvenance.Known)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.Should().Match<BuildingFootprint>(b =>
            b.Id == "sample" && b.HeightMetres == 40 && b.HeightProvenance == BuildingHeightProvenance.Known);
    }

    [Fact]
    public async Task SearchAsync_ShouldPreferAStoreyCount_OverTheBareDefault_ButKeepItAssumed()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", -40, -40, -20, -20, 54)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.Should().Match<BuildingFootprint>(b =>
            b.HeightMetres == 54 && b.HeightProvenance == BuildingHeightProvenance.Assumed);
    }

    [Fact]
    public async Task SearchAsync_ShouldNeverReplaceARecordedHeight()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered, Rect("tagged", -40, -40, -20, -20, 30, BuildingHeightProvenance.Known)));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", -40, -40, -20, -20, 60, BuildingHeightProvenance.Known)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.HeightMetres.Should().Be(30);
    }

    [Fact]
    public async Task SearchAsync_ShouldNotPassAHeightOn_ToABuildingItBarelyCovers()
    {
        // A tower's outline inside a mall's: the mall is not the tower's height.
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered, Rect("mall", -50, -50, 50, 50)));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("tower", -10, -10, 10, 10, 105, BuildingHeightProvenance.Known)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.HeightMetres.Should().Be(AssumedBuildingHeight.DefaultMetres);
    }

    [Fact]
    public async Task SearchAsync_ShouldPreferTheLargestSiteBuilding_OverAFragmentUnderTheSitePoint()
    {
        // BurJuman: the rendered map yields a tiny fragment under the site point; Overture has the tower.
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered, Rect("fragment", -2, -2, 2, 2, site: true), SampleBuilding));
        Returns(fallback, Found(BuildingFootprintSource.Overture, Rect("tower", -15, -15, 15, 15, 54, site: true)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings[0].Should().Match<BuildingFootprint>(b => b.Id == "tower" && b.IsSiteBuilding, "the site building leads the list");
        result.Buildings.Where(b => b.IsSiteBuilding).Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldHandTheSiteRole_ToTheKeptBuildingUnderALargerSiteCounterpart()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Found(BuildingFootprintSource.Rendered, Rect("mall", -50, -50, 50, 50), Rect("fragment", 60, 60, 63, 63, site: true)));
        Returns(fallback, Found(BuildingFootprintSource.Overture, Rect("tower", -15, -15, 15, 15, 54, site: true)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Select(b => (b.Id, b.IsSiteBuilding)).Should().Equal(("mall", true), ("fragment", false));
    }

    [Fact]
    public async Task SearchAsync_ShouldStopAddingBuildings_AtTheLimit()
    {
        var (primary, fallback, composite) = CreateComposite(maxBuildingCount: 1);
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        Returns(fallback, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", 50, 50, 70, 70)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("sample");
        result.Limited.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ShouldNotWaitPastTheStragglerBudget_ForASlowGapFiller()
    {
        var (primary, fallback, composite) = CreateComposite(stragglerBudget: TimeSpan.FromMilliseconds(50));
        Returns(primary, Found(BuildingFootprintSource.Rendered));
        var fallbackToken = CancellationToken.None;
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            fallbackToken = call.ArgAt<CancellationToken>(2);
            await Task.Delay(Timeout.Infinite, fallbackToken);
            return Empty(BuildingFootprintSource.Osm);
        });

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("sample");
        fallbackToken.IsCancellationRequested.Should().BeTrue("a source that is no longer waited for must not keep running");
    }

    [Fact]
    public async Task SearchAsync_ShouldPropagateTheCallersCancellation()
    {
        var (primary, fallback, composite) = CreateComposite();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(call => throw new OperationCanceledException(call.ArgAt<CancellationToken>(2)));
        Returns(fallback, Found(BuildingFootprintSource.Osm));

        var act = async () => await composite.SearchAsync(Center, 200, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyWithSourceNone_WhenBothSourcesReturnEmpty()
    {
        var (primary, fallback, composite) = CreateComposite();
        Returns(primary, Empty(BuildingFootprintSource.Rendered));
        Returns(fallback, Empty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().BeEmpty();
        result.Source.Should().Be(BuildingFootprintSource.None, "neither source actually supplied anything — the fallback's own Osm tag must not leak through on an empty result");
    }

    [Fact]
    public async Task SearchAsync_ShouldRethrow_WhenBothSourcesFail()
    {
        var (primary, fallback, composite) = CreateComposite();
        Unavailable(primary);
        Unavailable(fallback);

        var act = async () => await composite.SearchAsync(Center, 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>("every source failing must rethrow so the existing 503 Problem Details mapping still applies");
    }

    [Fact]
    public async Task Chain_ShouldUseOverture_WhenRenderedFindsNothing_AndFillItsGapsFromOsm()
    {
        var (rendered, overture, osm, composite) = CreateChain();
        Returns(rendered, Empty(BuildingFootprintSource.Rendered));
        Returns(overture, Found(BuildingFootprintSource.Overture));
        Returns(osm, Found(BuildingFootprintSource.Osm, Rect("osm_way_1", -40, -40, -20, -20), Rect("osm_way_2", 50, 50, 70, 70)));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Overture);
        result.Buildings.Select(b => b.Id).Should().Equal("sample", "osm_way_2");
    }

    [Fact]
    public async Task Chain_ShouldReachOsm_WhenRenderedAndOvertureAreBothUnavailable()
    {
        var (rendered, overture, osm, composite) = CreateChain();
        Unavailable(rendered);
        Unavailable(overture);
        Returns(osm, Found(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Osm);
    }

    [Fact]
    public async Task Chain_ShouldReturnSourceNone_WhenAMiddleSourceFails_AndTheOthersAreEmpty()
    {
        var (rendered, overture, osm, composite) = CreateChain();
        Returns(rendered, Empty(BuildingFootprintSource.Rendered));
        Unavailable(overture);
        Returns(osm, Empty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.None);
    }

    [Fact]
    public async Task Chain_ShouldReturnSourceNone_WhenTheLastSourceFails_ButAnEarlierOneAnsweredEmpty()
    {
        // specs/076 — an empty answer from rendered or Overture (which holds OSM's buildings) is a
        // real answer; the last source being down no longer turns it into an outage.
        var (rendered, overture, osm, composite) = CreateChain();
        Returns(rendered, Empty(BuildingFootprintSource.Rendered));
        Returns(overture, Empty(BuildingFootprintSource.Overture));
        Unavailable(osm);

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.None);
    }

    [Fact]
    public async Task Chain_ShouldRethrow_OnlyWhenEverySourceFails()
    {
        var (rendered, overture, osm, composite) = CreateChain();
        Unavailable(rendered);
        Unavailable(overture);
        Unavailable(osm);

        var act = async () => await composite.SearchAsync(Center, 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    /// <summary>
    /// T033, quickstart Scenario 8, SC-007, constitution §2.VIII — every failure this feature can
    /// produce resolves to a typed outcome the CALLER can act on: either a normal
    /// <see cref="BuildingFootprintResult"/> (including a legitimate empty one), or a rethrown
    /// <see cref="BuildingProviderUnavailableException"/> that the pre-existing (unchanged)
    /// <c>ProblemDetailsMiddleware</c> mapping already turns into a 503 Problem Details response —
    /// never a swallowed exception and never a result silently missing information.
    /// </summary>
    [Fact]
    public async Task FailureSurfaceSweep_EveryRowResolvesToATypedOutcome_NeverASilentFailure()
    {
        // Row: primary throws, fallback succeeds -> normal result, no exception reaches the caller.
        {
            var (primary, fallback, composite) = CreateComposite();
            Unavailable(primary);
            Returns(fallback, Found(BuildingFootprintSource.Osm));

            var result = await composite.SearchAsync(Center, 200, CancellationToken.None);
            result.Source.Should().Be(BuildingFootprintSource.Osm);
        }

        // Row: primary succeeds, fallback throws -> the primary's result, no exception.
        {
            var (primary, fallback, composite) = CreateComposite();
            Returns(primary, Found(BuildingFootprintSource.Rendered));
            Unavailable(fallback);

            var result = await composite.SearchAsync(Center, 200, CancellationToken.None);
            result.Source.Should().Be(BuildingFootprintSource.Rendered);
        }

        // Row: both empty -> a genuine empty success, Source = none, not an exception.
        {
            var (primary, fallback, composite) = CreateComposite();
            Returns(primary, Empty(BuildingFootprintSource.Rendered));
            Returns(fallback, Empty(BuildingFootprintSource.Osm));

            var result = await composite.SearchAsync(Center, 200, CancellationToken.None);
            result.Buildings.Should().BeEmpty();
            result.Source.Should().Be(BuildingFootprintSource.None);
        }

        // Row: both throw -> rethrows a typed exception the existing 503 mapping already handles.
        {
            var (primary, fallback, composite) = CreateComposite();
            Unavailable(primary);
            Unavailable(fallback);

            var act = async () => await composite.SearchAsync(Center, 200, CancellationToken.None);
            await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
        }
    }
}
