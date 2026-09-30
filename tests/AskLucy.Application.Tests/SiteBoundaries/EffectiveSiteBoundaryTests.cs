using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/079 — the outline in force: found, corrected, or found again when the link is dead or foreign (FR-026).</summary>
public sealed class EffectiveSiteBoundaryTests
{
    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211)];

    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();

    private EffectiveSiteBoundary Create() => new(_corrections);

    private static UserChat ChatWithBoundary(string userId = "user-1")
    {
        var chat = UserChat.Create("Chat", userId, null, userId);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.156, 55.221, FoundRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", userId);
        return chat;
    }

    private static SiteBoundaryCorrection CorrectionFor(string userId) =>
        SiteBoundaryCorrection.Create(
            userId, "Muscat Grand Mall", 25.156, 55.221,
            new FoundSiteBoundarySnapshot(FoundRing, [], null, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [EditedRing], 12_000, [], userId);

    [Fact]
    public async Task ResolveAsync_ShouldReturnNull_WhenTheChatHasNoOutline()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");

        (await Create().ResolveAsync(chat, TestContext.Current.CancellationToken)).Should().BeNull();
        (await Create().ResolveAsync(null, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheFoundOutline_WhenNotLinked()
    {
        var chat = ChatWithBoundary();

        var result = await Create().ResolveAsync(chat, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(chat.ActiveBoundary);
        await _corrections.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheCorrectedOutline_WhenLinkedToALiveCorrection()
    {
        var chat = ChatWithBoundary();
        var correction = CorrectionFor("user-1");
        chat.LinkSiteBoundaryCorrection(correction.Id, "user-1");
        _corrections.GetByIdAsync(correction.Id, "user-1", Arg.Any<CancellationToken>()).Returns(correction);

        var result = await Create().ResolveAsync(chat, TestContext.Current.CancellationToken);

        result!.IsHandEdited.Should().BeTrue();
        result.Polygon.Should().BeEquivalentTo(EditedRing);
        result.AreaSquareMeters.Should().Be(12_000);
        result.Revision.Should().Be(correction.Revision);
        chat.ActiveBoundary!.IsHandEdited.Should().BeFalse("the stored outline is never overwritten");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheFoundOutline_WhenTheCorrectionWasDeleted()
    {
        var chat = ChatWithBoundary();
        chat.LinkSiteBoundaryCorrection(Guid.NewGuid(), "user-1");
        _corrections.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SiteBoundaryCorrection?)null);

        var result = await Create().ResolveAsync(chat, TestContext.Current.CancellationToken);

        result!.IsHandEdited.Should().BeFalse();
        result.Polygon.Should().BeEquivalentTo(FoundRing);
    }

    [Fact]
    public async Task ResolveAsync_ShouldAskForTheChatOwnersCorrectionOnly_SoAForeignIdYieldsTheFoundOutline()
    {
        var chat = ChatWithBoundary("user-1");
        var foreign = CorrectionFor("someone-else");
        chat.LinkSiteBoundaryCorrection(foreign.Id, "user-1");
        // The repository scopes every read to the user id it is given, so another user's row is never returned.
        _corrections.GetByIdAsync(foreign.Id, "someone-else", Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await Create().ResolveAsync(chat, TestContext.Current.CancellationToken);

        await _corrections.Received(1).GetByIdAsync(foreign.Id, "user-1", Arg.Any<CancellationToken>());
        result!.IsHandEdited.Should().BeFalse();
        result.Polygon.Should().BeEquivalentTo(FoundRing);
    }
}
