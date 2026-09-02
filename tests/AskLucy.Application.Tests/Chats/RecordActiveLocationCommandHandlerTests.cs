using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats.Commands.RecordActiveLocation;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// specs/044-location-viewer-regression T014 (FR-009a/FR-009b, contracts S-1…S-4) — the
/// stale-boundary repair.
/// <para>
/// Before this, <c>ActiveBoundary</c> was only ever replaced on a *successful* resolution, so
/// navigating to a new site whose boundary failed left the previous site's boundary stored. The
/// next turn injected "a boundary is already shown for &lt;old site&gt;" into the prompt while the
/// viewer showed somewhere else. Clearing lives here — atomically with the location write — because
/// the case that matters is exactly the one where no boundary command ever arrives.
/// </para>
/// </summary>
public sealed class RecordActiveLocationCommandHandlerTests
{
    private static readonly IReadOnlyList<GeoPoint> SamplePolygon =
    [
        new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220),
    ];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly RecordActiveLocationCommandHandler _handler;
    private readonly Guid _chatId = Guid.NewGuid();

    public RecordActiveLocationCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new RecordActiveLocationCommandHandler(_chats, _unitOfWork, _currentUser);
    }

    private UserChat ChatWithBoundary(string siteName)
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            siteName, 25.156, 55.2218, SamplePolygon, 15_000, 0.9,
            BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", "user-1");
        _chats.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        return chat;
    }

    private Task HandleAsync(string locationName) =>
        _handler.Handle(
            new RecordActiveLocationCommand(_chatId, new ConfirmedLocationData(25.24, 55.30, locationName, 0.88)),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Handle_ShouldClearTheStoredBoundary_WhenTheConfirmedLocationNamesADifferentSite()
    {
        var chat = ChatWithBoundary("Al Safa Park 2");

        await HandleAsync("Zabeel Park");

        chat.ActiveBoundary.Should().BeNull("a stored boundary must never outlive the site it names");
        chat.ActiveLocation.Should().NotBeNull();
        chat.ActiveLocation!.LocationName.Should().Be("Zabeel Park");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// FR-009 must survive FR-009a: an over-eager clear would silently defeat boundary reuse and
    /// re-hit Overpass on every turn for the same site.
    /// </summary>
    [Theory]
    [InlineData("Al Safa Park 2")]
    [InlineData("al safa park 2")]
    [InlineData("AL SAFA PARK 2")]
    public async Task Handle_ShouldKeepTheStoredBoundary_WhenTheConfirmedLocationNamesTheSameSite(string locationName)
    {
        var chat = ChatWithBoundary("Al Safa Park 2");

        await HandleAsync(locationName);

        chat.ActiveBoundary.Should().NotBeNull("boundary reuse for a repeated site reference (FR-009) must still work");
        chat.ActiveBoundary!.SiteName.Should().Be("Al Safa Park 2");
    }

    [Fact]
    public async Task Handle_ShouldRecordTheLocation_WhenNoBoundaryIsStored()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        _chats.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);

        await HandleAsync("Zabeel Park");

        chat.ActiveLocation!.LocationName.Should().Be("Zabeel Park");
        chat.ActiveBoundary.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldDoNothing_WhenTheChatWasDeleted()
    {
        _chats.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var act = async () => await HandleAsync("Zabeel Park");

        await act.Should().NotThrowAsync();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
