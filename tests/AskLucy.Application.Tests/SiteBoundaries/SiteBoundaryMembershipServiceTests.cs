using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Ai;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/077 — which same-named buildings a resolved site proposes, and how its outline is redrawn from them.</summary>
public sealed class SiteBoundaryMembershipServiceTests
{
    private readonly IRelatedSiteBuildingProvider _provider = Substitute.For<IRelatedSiteBuildingProvider>();
    private readonly ISiteFootprintUnion _union = Substitute.For<ISiteFootprintUnion>();
    private readonly ICapabilitySettingsReader _settings = Substitute.For<ICapabilitySettingsReader>();
    private readonly SiteBoundaryMembershipService _service;

    private static readonly BoundaryCandidate Winner = new(
        "osm_way_100", new SiteBoundaryPolygon(Mall), SiteBoundarySource.OsmBoundary, "BurJuman Mall",
        new Dictionary<string, string> { ["name"] = "BurJuman Mall" }, 0, 10_000);

    public SiteBoundaryMembershipServiceTests()
    {
        _service = new SiteBoundaryMembershipService(_provider, _union, _settings, Substitute.For<ILogger<SiteBoundaryMembershipService>>());
        SettingIs(true);
    }

    [Fact]
    public void Classify_ProposesTheConnectedBuildingsAndListsTheRest()
    {
        Members.Select(m => (m.Name, m.Relation, m.Included)).Should().Equal(
            ("BurJuman Business Tower", SiteBoundaryMemberRelation.Connected, true),
            ("BurJuman Arjaan by Rotana", SiteBoundaryMemberRelation.Connected, true),
            ("Burjman Office Tower", SiteBoundaryMemberRelation.Nearby, false),
            ("BurJuman Metro Station", SiteBoundaryMemberRelation.Nearby, false));
    }

    [Fact]
    public void Classify_NeverCountsAStationAsConnected_EvenWhenItTouches()
    {
        var touching = MetroStation with { Ring = Rect(-40, 0, 0, 20) };

        SiteBoundaryMembershipService.Classify(Mall, [touching], SiteNameMatcher.CoresOf(["BurJuman Mall"]), [])
            .Should().ContainSingle().Which.Should().Match<SiteBoundaryMember>(m =>
                m.Relation == SiteBoundaryMemberRelation.Nearby && !m.Included);
    }

    [Fact]
    public void Classify_SkipsTheSiteItselfAndWhatLiesInsideIt()
    {
        var ownWay = OfficeTower with { Id = "osm_way_100" };
        var inside = OfficeTower with { Id = "osm_way_7", Ring = Rect(40, 40, 60, 60) };

        SiteBoundaryMembershipService.Classify(Mall, [ownWay, inside], SiteNameMatcher.CoresOf(["BurJuman Mall"]), ["osm_way_100"])
            .Should().BeEmpty();
    }

    [Fact]
    public async Task WithRelatedBuildingsAsync_LeavesTheSiteAloneWhenTheSettingIsOff()
    {
        SettingIs(false);
        var boundary = Boundary();

        var result = await _service.WithRelatedBuildingsAsync(boundary, Winner, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(boundary);
        await _provider.DidNotReceiveWithAnyArgs().FindNamedBuildingsAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WithRelatedBuildingsAsync_KeepsTheSiteOutlineWhenTheSearchFails()
    {
        _provider.FindNamedBuildingsAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BoundaryProviderUnavailableException("Overpass is down."));
        var boundary = Boundary();

        var result = await _service.WithRelatedBuildingsAsync(boundary, Winner, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(boundary);
    }

    [Fact]
    public async Task WithRelatedBuildingsAsync_JoinsTheConnectedBuildingsAndRemembersTheMallAlone()
    {
        _provider.FindNamedBuildingsAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Everything);
        var joined = Rect(0, 0, 130, 100);
        _union.Union(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), SiteBoundaryMembershipService.ConnectedGapMeters).Returns([joined]);

        var result = await _service.WithRelatedBuildingsAsync(Boundary(), Winner, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Polygon.Should().BeSameAs(joined);
        result.CorePolygon.Should().BeSameAs(Mall);
        result.AdditionalPolygons.Should().BeEmpty();
        result.Members.Should().HaveCount(4);
        _union.Received(1).Union(
            Arg.Is<IReadOnlyList<IReadOnlyList<GeoPoint>>>(rings => rings != null && rings.Count == 3), SiteBoundaryMembershipService.ConnectedGapMeters);
    }

    [Fact]
    public void Compose_KeepsTheRingHoldingTheSiteAsTheOutlineAndTheRestAsAdditional()
    {
        var mallAndPodium = Rect(0, 0, 130, 100);
        var across = TowerAcrossTheStreet.Ring;
        _union.Union(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>()).Returns([across, mallAndPodium]);
        var members = Members.Select(m => m with { Included = m.Kind == SiteBoundaryMemberKind.Building }).ToList();

        var result = _service.Compose(Boundary(), members);

        result.Polygon.Should().BeSameAs(mallAndPodium);
        result.AdditionalPolygons.Should().ContainSingle().Which.Should().BeSameAs(across);
        result.AreaSquareMeters.Should().BeApproximately(
            GeometryMath.AreaSquareMeters(mallAndPodium) + GeometryMath.AreaSquareMeters(across), 1);
    }

    [Fact]
    public void Compose_WithNothingIncluded_IsTheSiteAloneWithoutAUnion()
    {
        var result = _service.Compose(Boundary(), [.. Members.Select(m => m with { Included = false })]);

        result.Polygon.Should().BeSameAs(Mall);
        result.AdditionalPolygons.Should().BeEmpty();
        result.Members.Should().OnlyContain(m => !m.Included);
        _union.DidNotReceiveWithAnyArgs().Union(default!, default);
    }

    [Fact]
    public void Compose_FallsBackToTheMappedRingsWhenTheUnionTracesNothing()
    {
        _union.Union(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>()).Returns([]);

        var result = _service.Compose(Boundary(), Members);

        result.Polygon.Should().BeSameAs(Mall);
        result.AdditionalPolygons.Should().HaveCount(2);
    }

    private void SettingIs(bool on) =>
        _settings.GetBooleanAsync(AiCapability.BoundaryVision, CapabilitySettingCatalog.IncludeConnectedBuildingsKey, Arg.Any<CancellationToken>())
            .Returns(on);
}
