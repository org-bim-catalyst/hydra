using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

/// <summary>specs/079 — <see cref="ActiveSiteBoundary.WithCorrection"/> and the chat's link to a correction.</summary>
public sealed class ActiveSiteBoundaryCorrectionTests
{
    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211)];

    private static readonly IReadOnlyList<GeoPoint> SecondRing =
        [new(25.1570, 55.2230), new(25.1570, 55.2235), new(25.1565, 55.2235)];

    private static UserChat ChatWithBoundary()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.156, 55.221, FoundRing, 15_000, 0.7,
            BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", "user-1",
            corePolygon: FoundRing);
        return chat;
    }

    private static SiteBoundaryCorrection CorrectionFor(UserChat chat) =>
        SiteBoundaryCorrection.Create(
            "user-1", chat.ActiveBoundary!.SiteName, 25.156, 55.221,
            new FoundSiteBoundarySnapshot(FoundRing, [], FoundRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [EditedRing, SecondRing], 12_345, [], "user-1");

    [Fact]
    public void WithCorrection_ShouldReplaceShapeAreaAndRevisionButKeepIdentity()
    {
        var chat = ChatWithBoundary();
        var correction = CorrectionFor(chat);

        var effective = chat.ActiveBoundary!.WithCorrection(correction);

        effective.Polygon.Should().BeEquivalentTo(EditedRing);
        effective.AdditionalPolygons.Should().ContainSingle().Which.Should().BeEquivalentTo(SecondRing);
        effective.AreaSquareMeters.Should().Be(12_345);
        effective.Source.Should().Be(SiteBoundarySource.UserCorrected);
        effective.IsHandEdited.Should().BeTrue();
        effective.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
        effective.Revision.Should().Be(correction.Revision);
        effective.CorrectionId.Should().Be(correction.Id);
        effective.SiteName.Should().Be("Muscat Grand Mall");
        effective.CorePolygon.Should().BeEquivalentTo(FoundRing);
        effective.CentroidLatitude.Should().Be(25.156);
    }

    [Fact]
    public void WithCorrection_ShouldNotTouchTheStoredBoundary()
    {
        var chat = ChatWithBoundary();

        _ = chat.ActiveBoundary!.WithCorrection(CorrectionFor(chat));

        chat.ActiveBoundary!.Polygon.Should().BeEquivalentTo(FoundRing);
        chat.ActiveBoundary.IsHandEdited.Should().BeFalse();
        chat.ActiveBoundary.AreaSquareMeters.Should().Be(15_000);
    }

    [Fact]
    public void SetActiveBoundary_ShouldAssignARevision()
    {
        ChatWithBoundary().ActiveBoundary!.Revision.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void LinkAndUnlink_ShouldSetTheLinkAndRegenerateTheRevision()
    {
        var chat = ChatWithBoundary();
        var initial = chat.ActiveBoundary!.Revision;
        var correctionId = Guid.NewGuid();

        chat.LinkSiteBoundaryCorrection(correctionId, "user-1");
        chat.ActiveBoundary!.CorrectionId.Should().Be(correctionId);
        var linked = chat.ActiveBoundary.Revision;
        linked.Should().NotBe(initial);

        chat.UnlinkSiteBoundaryCorrection("user-1");
        chat.ActiveBoundary!.CorrectionId.Should().BeNull();
        chat.ActiveBoundary.Revision.Should().NotBe(linked);
    }

    [Fact]
    public void Link_ShouldBeRefused_WhenTheChatHasNoOutline()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        var act = () => chat.LinkSiteBoundaryCorrection(Guid.NewGuid(), "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Unlink_ShouldDoNothing_WhenNotLinked()
    {
        var chat = ChatWithBoundary();
        var revision = chat.ActiveBoundary!.Revision;

        chat.UnlinkSiteBoundaryCorrection("user-1");

        chat.ActiveBoundary!.Revision.Should().Be(revision);
    }

    [Fact]
    public void RecordingADifferentSite_ShouldClearTheLink()
    {
        var chat = ChatWithBoundary();
        chat.LinkSiteBoundaryCorrection(Guid.NewGuid(), "user-1");

        chat.SetActiveBoundary(
            "BurJuman", 25.25, 55.30, FoundRing, 20_000, 0.9, BoundaryConfidenceLevel.High,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", "user-1");

        chat.ActiveBoundary!.CorrectionId.Should().BeNull();
    }
}
