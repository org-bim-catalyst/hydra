using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// specs/079 — the outline in force for a chat is the one Lucy found with the user's hand edit
/// applied. The projection lives in Application rather than on <c>ActiveSiteBoundary</c> so the
/// chat's value object does not have to know another bounded context's aggregate root; the
/// behaviour is unchanged, including that it is pure — the stored outline is never overwritten.
/// </summary>
public sealed class CorrectionOutlineTests
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
    public void ApplyCorrection_ShouldReplaceShapeAreaAndRevisionButKeepIdentity()
    {
        var chat = ChatWithBoundary();
        var correction = CorrectionFor(chat);

        var effective = chat.ActiveBoundary!.ApplyCorrection(correction);

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
    public void ApplyCorrection_ShouldNotTouchTheStoredBoundary()
    {
        var chat = ChatWithBoundary();

        _ = chat.ActiveBoundary!.ApplyCorrection(CorrectionFor(chat));

        chat.ActiveBoundary!.Polygon.Should().BeEquivalentTo(FoundRing);
        chat.ActiveBoundary.IsHandEdited.Should().BeFalse();
        chat.ActiveBoundary.AreaSquareMeters.Should().Be(15_000);
    }
}
