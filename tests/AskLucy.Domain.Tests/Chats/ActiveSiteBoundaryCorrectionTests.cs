using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

/// <summary>
/// specs/079 — the chat's link to a correction. The projection that applies a correction to the
/// outline in force moved to <c>CorrectionOutline.ApplyCorrection</c> in Application (the value
/// object must not know another bounded context's aggregate root); its tests live there.
/// </summary>
public sealed class ActiveSiteBoundaryCorrectionTests
{
    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220)];

    private static UserChat ChatWithBoundary()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.156, 55.221, FoundRing, 15_000, 0.7,
            BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", "user-1",
            corePolygon: FoundRing);
        return chat;
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
