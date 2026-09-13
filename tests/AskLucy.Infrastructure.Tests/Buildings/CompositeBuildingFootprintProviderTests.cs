using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Buildings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/053-rendered-building-footprints T027-T028, contracts/footprint-source-arbitration.md —
/// <see cref="CompositeBuildingFootprintProvider"/>'s arbitration table, exercised with two faked
/// <see cref="IBuildingFootprintProvider"/> instances (no HTTP, no image decoding — that belongs to
/// each inner provider's own tests). The composite depends on the INTERFACE via keyed DI, not on
/// either concrete provider type directly (corrected during implementation — both concrete
/// providers are <c>sealed</c>, which NSubstitute cannot proxy), which is what makes this level of
/// test possible at all.
/// </summary>
public sealed class CompositeBuildingFootprintProviderTests
{
    private static readonly GeoPoint Center = new(25.1560, 55.2218);

    private static readonly BuildingFootprint SampleBuilding = new(
        Id: "sample",
        Ring: [new GeoPoint(25.1560, 55.2210), new GeoPoint(25.1560, 55.2220), new GeoPoint(25.1550, 55.2220), new GeoPoint(25.1550, 55.2210), new GeoPoint(25.1560, 55.2210)],
        HeightMetres: 9.0,
        HeightProvenance: BuildingHeightProvenance.Assumed,
        Name: string.Empty,
        IsSiteBuilding: false);

    private static BuildingFootprintResult NonEmpty(BuildingFootprintSource source, BuildingFootprint? building = null) =>
        new([building ?? SampleBuilding], Limited: false, ExcludedCount: 0, RadiusMetres: 200, source);

    private static BuildingFootprintResult Empty(BuildingFootprintSource source) =>
        new([], Limited: false, ExcludedCount: 0, RadiusMetres: 200, source);

    private static (IBuildingFootprintProvider Primary, IBuildingFootprintProvider Fallback, CompositeBuildingFootprintProvider Composite) CreateComposite()
    {
        var primary = Substitute.For<IBuildingFootprintProvider>();
        var fallback = Substitute.For<IBuildingFootprintProvider>();
        var composite = new CompositeBuildingFootprintProvider(primary, fallback, NullLogger<CompositeBuildingFootprintProvider>.Instance);
        return (primary, fallback, composite);
    }

    [Fact]
    public async Task SearchAsync_ShouldUseThePrimaryResult_WhenItReturnsFootprints_AndNeverCallTheFallback()
    {
        var (primary, fallback, composite) = CreateComposite();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Rendered));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Rendered);
        result.Buildings.Should().ContainSingle();
        await fallback.DidNotReceive().SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ShouldFallBackToOsm_WhenThePrimaryReturnsEmpty()
    {
        var (primary, fallback, composite) = CreateComposite();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Empty(BuildingFootprintSource.Rendered));
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Osm);
        result.Buildings.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldFallBackToOsm_WhenThePrimaryThrows_AndSurfaceNoError()
    {
        var (primary, fallback, composite) = CreateComposite();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("rendered unavailable"));
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Source.Should().Be(BuildingFootprintSource.Osm);
        result.Buildings.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyWithSourceNone_WhenBothSourcesReturnEmpty()
    {
        var (primary, fallback, composite) = CreateComposite();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Empty(BuildingFootprintSource.Rendered));
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Empty(BuildingFootprintSource.Osm));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().BeEmpty();
        result.Source.Should().Be(BuildingFootprintSource.None, "neither source actually supplied anything — the fallback's own Osm tag must not leak through on an empty result");
    }

    [Fact]
    public async Task SearchAsync_ShouldRethrow_WhenBothSourcesFail()
    {
        var (primary, fallback, composite) = CreateComposite();
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("rendered unavailable"));
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
            .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("osm unavailable too"));

        var act = async () => await composite.SearchAsync(Center, 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>("both sources failing must rethrow so the existing 503 Problem Details mapping still applies");
    }

    [Fact]
    public async Task SearchAsync_ShouldNeverMergeResults_WhenBothSourcesHoldFootprints()
    {
        // FR-014 — the composite never even calls the fallback once the primary succeeds (asserted
        // above), but this pins the stronger invariant directly: the result is always exactly one
        // source's buildings, never a union, regardless of how arbitration is later refactored.
        var (primary, fallback, composite) = CreateComposite();
        var secondBuilding = SampleBuilding with { Id = "second" };
        primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Rendered));
        fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Osm, secondBuilding));

        var result = await composite.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle().Which.Id.Should().Be("sample", "the primary's result wins outright — the fallback's building must not appear alongside it");
    }

    /// <summary>
    /// T033, quickstart Scenario 8, SC-007, constitution §2.VIII — every failure this feature can
    /// produce resolves to a typed outcome the CALLER can act on: either a normal
    /// <see cref="BuildingFootprintResult"/> (including a legitimate empty one), or a rethrown
    /// <see cref="BuildingProviderUnavailableException"/> that the pre-existing (unchanged)
    /// <c>ProblemDetailsMiddleware</c> mapping already turns into a 503 Problem Details response —
    /// never a swallowed exception and never a result silently missing information. Each row here
    /// is proven individually elsewhere in this file and in
    /// <see cref="RenderedBuildingFootprintProviderTests"/>; this walks the whole table in one
    /// place so the failure surface stays traceable to the requirement rather than scattered.
    /// </summary>
    [Fact]
    public async Task FailureSurfaceSweep_EveryRowResolvesToATypedOutcome_NeverASilentFailure()
    {
        // Row: primary throws, fallback succeeds -> normal result, no exception reaches the caller.
        {
            var (primary, fallback, composite) = CreateComposite();
            primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
                .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("rendered down"));
            fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(NonEmpty(BuildingFootprintSource.Osm));

            var result = await composite.SearchAsync(Center, 200, CancellationToken.None);
            result.Source.Should().Be(BuildingFootprintSource.Osm);
        }

        // Row: both empty -> a genuine empty success, Source = none, not an exception.
        {
            var (primary, fallback, composite) = CreateComposite();
            primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Empty(BuildingFootprintSource.Rendered));
            fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>()).Returns(Empty(BuildingFootprintSource.Osm));

            var result = await composite.SearchAsync(Center, 200, CancellationToken.None);
            result.Buildings.Should().BeEmpty();
            result.Source.Should().Be(BuildingFootprintSource.None);
        }

        // Row: both throw -> rethrows a typed exception the existing 503 mapping already handles.
        {
            var (primary, fallback, composite) = CreateComposite();
            primary.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
                .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("rendered down"));
            fallback.SearchAsync(Center, 200, Arg.Any<CancellationToken>())
                .Returns<Task<BuildingFootprintResult>>(_ => throw new BuildingProviderUnavailableException("osm down"));

            var act = async () => await composite.SearchAsync(Center, 200, CancellationToken.None);
            await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
        }
    }
}
