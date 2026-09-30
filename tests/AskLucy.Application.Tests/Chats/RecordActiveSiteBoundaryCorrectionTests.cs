using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats.Commands.RecordActiveSiteBoundary;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// specs/079 - recording a reused hand-edited outline must link the chat to it and keep the outline
/// Lucy found, never store the user's rings as if she had found them (that would make Reset impossible).
/// </summary>
public sealed class RecordActiveSiteBoundaryCorrectionTests
{
    private const string UserId = "user-1";

    private static readonly IReadOnlyList<GeoPoint> Found =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> Edited =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1561, 55.2211)];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly UserChat _chat = UserChat.Create("Chat", UserId, null, UserId);

    public RecordActiveSiteBoundaryCorrectionTests()
    {
        _currentUser.UserId.Returns(UserId);
        _chats.GetByIdAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns(_chat);
    }

    private RecordActiveSiteBoundaryCommandHandler Create() =>
        new(_chats, _corrections, _unitOfWork, _currentUser, NullLogger<RecordActiveSiteBoundaryCommandHandler>.Instance);

    private static SiteBoundaryCorrection Correction() =>
        SiteBoundaryCorrection.Create(
            UserId, "Muscat Grand Mall", 25.1555, 55.2215,
            new FoundSiteBoundarySnapshot(Found, [], null, 15_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [Edited], 12_345, [], UserId);

    private static ConfirmedSiteBoundaryData Reused(SiteBoundaryCorrection correction) => CorrectionOutline.ToConfirmed(correction);

    [Fact]
    public async Task AReusedCorrection_StoresWhatLucyFoundAndLinksTheChat()
    {
        var correction = Correction();
        _corrections.GetByIdAsync(correction.Id, UserId, Arg.Any<CancellationToken>()).Returns(correction);

        await Create().Handle(new RecordActiveSiteBoundaryCommand(_chat.Id, Reused(correction)), TestContext.Current.CancellationToken);

        _chat.ActiveBoundary!.Polygon.Should().BeEquivalentTo(Found, "the chat keeps the outline Lucy found");
        _chat.ActiveBoundary.AreaSquareMeters.Should().Be(15_000);
        _chat.ActiveBoundary.Source.Should().Be(SiteBoundarySource.OsmBoundary);
        _chat.ActiveBoundary.CorrectionId.Should().Be(correction.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterLinking_TheOutlineInForceIsTheUsers()
    {
        var correction = Correction();
        _corrections.GetByIdAsync(correction.Id, UserId, Arg.Any<CancellationToken>()).Returns(correction);
        await Create().Handle(new RecordActiveSiteBoundaryCommand(_chat.Id, Reused(correction)), TestContext.Current.CancellationToken);

        var inForce = await new EffectiveSiteBoundary(_corrections).ResolveAsync(_chat, TestContext.Current.CancellationToken);

        inForce!.IsHandEdited.Should().BeTrue();
        inForce.Polygon.Should().BeEquivalentTo(Edited);
        inForce.AreaSquareMeters.Should().Be(12_345);
    }

    [Fact]
    public async Task ACorrectionThatIsGone_LeavesTheChatUntouched_AndSavesNothing()
    {
        var correction = Correction();
        _corrections.GetByIdAsync(correction.Id, UserId, Arg.Any<CancellationToken>()).Returns((SiteBoundaryCorrection?)null);

        await Create().Handle(new RecordActiveSiteBoundaryCommand(_chat.Id, Reused(correction)), TestContext.Current.CancellationToken);

        _chat.ActiveBoundary.Should().BeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUsersCorrectionId_CannotBeLinked()
    {
        var correction = Correction();
        // The lookup is by this user's id, so a row owned by someone else is not found.
        _corrections.GetByIdAsync(correction.Id, UserId, Arg.Any<CancellationToken>()).Returns((SiteBoundaryCorrection?)null);
        _corrections.GetByIdAsync(correction.Id, "someone-else", Arg.Any<CancellationToken>()).Returns(correction);

        await Create().Handle(new RecordActiveSiteBoundaryCommand(_chat.Id, Reused(correction)), TestContext.Current.CancellationToken);

        _chat.ActiveBoundary.Should().BeNull();
    }

    [Fact]
    public async Task AnOrdinaryBoundary_IsStoredAsBefore_WithNoLink()
    {
        var found = new ConfirmedSiteBoundaryData(
            "Muscat Grand Mall", 25.1555, 55.2215, Found, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", []);

        await Create().Handle(new RecordActiveSiteBoundaryCommand(_chat.Id, found), TestContext.Current.CancellationToken);

        _chat.ActiveBoundary!.Polygon.Should().BeEquivalentTo(Found);
        _chat.ActiveBoundary.CorrectionId.Should().BeNull();
        await _corrections.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default!, default);
    }

    [Fact]
    public async Task AChatThatWasDeleted_IsIgnored()
    {
        var correction = Correction();

        await Create().Handle(new RecordActiveSiteBoundaryCommand(Guid.NewGuid(), Reused(correction)), TestContext.Current.CancellationToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
