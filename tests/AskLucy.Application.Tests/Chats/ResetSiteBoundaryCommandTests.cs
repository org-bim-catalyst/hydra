using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.ResetSiteBoundary;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>specs/079 (US5) - the reset handler's ownership, not-edited, revision and persistence rules.</summary>
public sealed class ResetSiteBoundaryCommandTests
{
    private const string Owner = "owner-1";

    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1550, 55.2210), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211), new(25.1561, 55.2211)];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IRoleAuditLogRepository _audit = Substitute.For<IRoleAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public ResetSiteBoundaryCommandTests() => _currentUser.UserId.Returns(Owner);

    private ResetSiteBoundaryCommandHandler CreateHandler() => new(
        _chats, _corrections, _messages,
        new EffectiveSiteBoundary(_corrections),
        new ChatOwnershipAuditor(_audit, _unitOfWork),
        _unitOfWork, _currentUser);

    private UserChat ChatWithOutline(out SiteBoundaryCorrection? correction, string owner = Owner, bool handEdited = false)
    {
        var chat = UserChat.Create("Chat", owner, null, owner);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, FoundRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        correction = null;
        if (handEdited)
        {
            var found = chat.ActiveBoundary!;
            var snapshot = new FoundSiteBoundarySnapshot(
                found.Polygon, found.AdditionalPolygons, found.CorePolygon, found.AreaSquareMeters, found.Confidence,
                found.ConfidenceLevel, found.Source, found.SourceDetail, found.Members);
            correction = SiteBoundaryCorrection.Create(
                owner, found.SiteName, found.CentroidLatitude, found.CentroidLongitude, snapshot, [EditedRing], 14_321.5, found.Members, owner);
            chat.LinkSiteBoundaryCorrection(correction.Id, owner);
            _corrections.GetByIdAsync(correction.Id, owner, Arg.Any<CancellationToken>()).Returns(correction);
        }

        return chat;
    }

    private async Task<string> RevisionInForceAsync(UserChat chat) =>
        (await new EffectiveSiteBoundary(_corrections).ResolveAsync(chat, TestContext.Current.CancellationToken))!.Revision.ToString();

    [Fact]
    public void Validator_ShouldRejectARevisionThatIsNotAnIdentifier() =>
        new ResetSiteBoundaryCommandValidator().Validate(new ResetSiteBoundaryCommand(Guid.NewGuid(), "nope")).IsValid.Should().BeFalse();

    [Fact]
    public async Task Handle_ShouldDeleteTheCorrection_UnlinkTheChatAndAppendOneLine_InOneSave()
    {
        var chat = ChatWithOutline(out var correction, handEdited: true);
        Domain.Chats.Message? line = null;
        _messages.When(r => r.Add(Arg.Any<Domain.Chats.Message>())).Do(call => line = call.Arg<Domain.Chats.Message>());

        var result = await CreateHandler().Handle(
            new ResetSiteBoundaryCommand(chat.Id, await RevisionInForceAsync(chat)), TestContext.Current.CancellationToken);

        correction!.DeletedAtUtc.Should().NotBeNull();
        chat.ActiveBoundary!.CorrectionId.Should().BeNull();
        result.ActiveBoundary.IsHandEdited.Should().BeFalse();
        result.ActiveBoundary.AreaSquareMeters.Should().Be(15_000);
        line!.Content.Should().Contain("back to the one I found").And.Contain("15,000");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTheOutlineWasNeverEdited()
    {
        var chat = ChatWithOutline(out _);

        var act = () => CreateHandler().Handle(
            new ResetSiteBoundaryCommand(chat.Id, chat.ActiveBoundary!.Revision.ToString()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowAConflictCarryingTheCurrentRevision_WhenTheRevisionIsStale()
    {
        var chat = ChatWithOutline(out var correction, handEdited: true);

        var act = () => CreateHandler().Handle(
            new ResetSiteBoundaryCommand(chat.Id, Guid.NewGuid().ToString()), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        thrown.Which.CurrentRevision.Should().Be(await RevisionInForceAsync(chat));
        correction!.DeletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldTurnANonOwnerAwayWithNotFound_AndAuditIt()
    {
        var chat = ChatWithOutline(out _, "someone-else");

        var act = () => CreateHandler().Handle(
            new ResetSiteBoundaryCommand(chat.Id, Guid.NewGuid().ToString()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _audit.Received(1).Add(Arg.Is<RoleAuditLog>(e => e!.Action == RoleAuditAction.AuthorizationDenied && e.ActorUserId == Owner));
    }
}
