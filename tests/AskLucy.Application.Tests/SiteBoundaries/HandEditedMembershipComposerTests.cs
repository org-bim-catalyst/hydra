using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;
using static AskLucy.Application.Tests.SiteBoundaries.BurJumanSite;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/079 (US4, research D9) - which geometry operation a building choice becomes on a hand-edited outline.</summary>
public sealed class HandEditedMembershipComposerTests
{
    private static readonly IReadOnlyList<GeoPoint> Edited = Rect(0, 0, 100, 100);
    private static readonly IReadOnlyList<GeoPoint> Joined = Rect(0, 0, 130, 100);

    private readonly ISiteRingGeometry _geometry = Substitute.For<ISiteRingGeometry>();
    private readonly HandEditedMembershipComposer _composer;

    public HandEditedMembershipComposerTests()
    {
        _composer = new HandEditedMembershipComposer(_geometry);
        _geometry.Intersects(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>())
            .Returns(false);
    }

    private static SiteBoundaryMember Member(string id, SiteBoundaryMemberRelation relation, IReadOnlyList<GeoPoint> ring, bool included) =>
        new(id, id, SiteBoundaryMemberKind.Building, relation, relation == SiteBoundaryMemberRelation.Connected ? 0.5 : 26, ring, included);

    private void Touching() =>
        _geometry.Intersects(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>())
            .Returns(true);

    [Fact]
    public void AddingAConnectedMember_JoinsItOntoTheRingItTouches()
    {
        Touching();
        _geometry.Join(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(Joined);
        var tower = Member("t", SiteBoundaryMemberRelation.Connected, Rect(100, 0, 130, 30), included: false);

        var result = _composer.Apply([Edited], [tower], [tower with { Included = true }]);

        result.Succeeded.Should().BeTrue();
        result.Rings.Should().HaveCount(1);
        _geometry.Received(1).Join(Arg.Any<IReadOnlyList<GeoPoint>>(), tower.Ring);
    }

    [Fact]
    public void AddingASeparateMember_AddsItAsARing()
    {
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), CombineOperation.Add)
            .Returns(new CombineResult(CombineFailure.None, [Edited, Rect(0, 126, 30, 156)]));
        var across = Member("a", SiteBoundaryMemberRelation.Nearby, Rect(0, 126, 30, 156), included: false);

        var result = _composer.Apply([Edited], [across], [across with { Included = true }]);

        result.Rings.Should().HaveCount(2);
        _geometry.DidNotReceive().Join(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>());
    }

    [Fact]
    public void RemovingAConnectedMember_CutsItOffTheRing()
    {
        Touching();
        _geometry.Cut(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(Edited);
        var tower = Member("t", SiteBoundaryMemberRelation.Connected, Rect(100, 0, 130, 30), included: true);

        var result = _composer.Apply([Joined], [tower], [tower with { Included = false }]);

        result.Succeeded.Should().BeTrue();
        _geometry.Received(1).Cut(Arg.Any<IReadOnlyList<GeoPoint>>(), tower.Ring);
    }

    [Fact]
    public void RemovingASeparateMember_DropsItsRing()
    {
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), CombineOperation.Cut)
            .Returns(new CombineResult(CombineFailure.None, [Edited]));
        var across = Member("a", SiteBoundaryMemberRelation.Nearby, Rect(0, 126, 30, 156), included: true);

        var result = _composer.Apply([Edited, across.Ring], [across], [across with { Included = false }]);

        result.Rings.Should().HaveCount(1);
    }

    [Fact]
    public void AMemberWhoseChoiceDidNotChange_LeavesTheRingsAlone()
    {
        var tower = Member("t", SiteBoundaryMemberRelation.Connected, Rect(100, 0, 130, 30), included: true);

        var result = _composer.Apply([Edited], [tower], [tower]);

        result.Rings.Should().HaveCount(1);
        _geometry.DidNotReceive().Join(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>());
        _geometry.DidNotReceive().Cut(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<GeoPoint>>());
    }

    [Fact]
    public void SaysSoPlainly_WhenTakingTheMemberOutWouldLeaveNothing()
    {
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), CombineOperation.Cut)
            .Returns(new CombineResult(CombineFailure.NothingLeft, []));
        var across = Member("a", SiteBoundaryMemberRelation.Nearby, Edited, included: true);

        var result = _composer.Apply([Edited], [across], [across with { Included = false }]);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Contain("would leave nothing");
    }
}
