using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>specs/079 - validator bounds and the handler's ownership, revision, geometry and persistence rules.</summary>
public sealed class SaveSiteBoundaryEditCommandTests
{
    private const string Owner = "owner-1";

    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1550, 55.2210), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1551, 55.2211), new(25.1561, 55.2211)];

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly ISiteRingGeometry _geometry = Substitute.For<ISiteRingGeometry>();
    private readonly IRoleAuditLogRepository _audit = Substitute.For<IRoleAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public SaveSiteBoundaryEditCommandTests()
    {
        _currentUser.UserId.Returns(Owner);
        _geometry.Validate(Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(RingValidationResult.Ok);
        _geometry.ValidateVoids(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>())
            .Returns(new VoidValidation(RingValidationResult.Ok, -1));
        _geometry.Intersects(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<double>())
            .Returns(true);
        _geometry.UnionArea(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>()).Returns(14_321.5);
    }

    private SaveSiteBoundaryEditCommandHandler CreateHandler() => new(
        _chats, _corrections, _messages, _geometry,
        new EffectiveSiteBoundary(_corrections),
        new ChatOwnershipAuditor(_audit, _unitOfWork),
        _unitOfWork, _currentUser);

    private UserChat ChatWithOutline(string owner = Owner)
    {
        var chat = UserChat.Create("Chat", owner, null, owner);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, FoundRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        return chat;
    }

    private static SaveSiteBoundaryEditCommand CommandFor(UserChat chat, string? revision = null) =>
        new(chat.Id, revision ?? chat.ActiveBoundary!.Revision.ToString(), [EditedRing]);

    // ---- validator -------------------------------------------------------

    private static readonly SaveSiteBoundaryEditCommandValidator Validator = new();

    private static IReadOnlyList<GeoPoint> Ring(int corners) =>
        [.. Enumerable.Range(0, corners).Select(i => new GeoPoint(25 + (i * 1e-5), 55 + (i % 2 * 1e-5)))];

    private static SaveSiteBoundaryEditCommand Valid(IReadOnlyList<IReadOnlyList<GeoPoint>>? rings = null, string? revision = null) =>
        new(Guid.NewGuid(), revision ?? Guid.NewGuid().ToString(), rings ?? [Ring(4)]);

    [Fact]
    public void Validator_ShouldAcceptAWellFormedRequest() =>
        Validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validator_ShouldRejectNoRings_AndMoreThanTwenty()
    {
        Validator.Validate(Valid([])).IsValid.Should().BeFalse();
        Validator.Validate(Valid([.. Enumerable.Range(0, 21).Select(_ => Ring(4))])).IsValid.Should().BeFalse();
        Validator.Validate(Valid([.. Enumerable.Range(0, 20).Select(_ => Ring(4))])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldBoundTheCornersPerRing()
    {
        Validator.Validate(Valid([Ring(2)])).IsValid.Should().BeFalse();
        Validator.Validate(Valid([Ring(3)])).IsValid.Should().BeTrue();
        Validator.Validate(Valid([Ring(2_000)])).IsValid.Should().BeTrue();
        Validator.Validate(Valid([Ring(2_001)])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldNotCountARepeatedClosingCorner()
    {
        var closed = Ring(2_000).Append(Ring(2_000)[0]).ToList();
        Validator.Validate(Valid([closed])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldBoundTheTotalAtFiveThousand()
    {
        Validator.Validate(Valid([Ring(2_000), Ring(2_000), Ring(1_000)])).IsValid.Should().BeTrue();
        Validator.Validate(Valid([Ring(2_000), Ring(2_000), Ring(1_001)])).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(double.NaN, 55)]
    [InlineData(91, 55)]
    [InlineData(25, 181)]
    [InlineData(25, double.PositiveInfinity)]
    public void Validator_ShouldRejectOutOfRangeCoordinates(double latitude, double longitude)
    {
        IReadOnlyList<GeoPoint> ring = [new(latitude, longitude), new(25, 55), new(26, 56)];
        Validator.Validate(Valid([ring])).IsValid.Should().BeFalse();
    }

    // ---- specs/081: voids ------------------------------------------------

    private static readonly IReadOnlyList<GeoPoint> VoidRing =
        [new(25.1558, 55.2214), new(25.1558, 55.2216), new(25.1556, 55.2216)];

    [Fact]
    public void Validator_ShouldAcceptVoids_AndBoundThem()
    {
        var valid = Valid() with { Voids = [[VoidRing]] };
        Validator.Validate(valid).IsValid.Should().BeTrue();

        Validator.Validate(valid with { Voids = [[VoidRing], [VoidRing]] }).IsValid.Should().BeFalse();
        Validator.Validate(valid with { Voids = [[[VoidRing[0], VoidRing[1]]]] }).IsValid.Should().BeFalse();
        Validator.Validate(valid with { Voids = [[.. Enumerable.Range(0, 51).Select(_ => VoidRing)]] }).IsValid.Should().BeFalse();
        Validator.Validate(valid with { Voids = [[.. Enumerable.Range(0, 50).Select(_ => VoidRing)]] }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldCountVoidCornersTowardTheTotal()
    {
        var nearlyFull = Valid([.. Enumerable.Range(0, 2).Select(_ => Ring(2_000))]);
        Validator.Validate(nearlyFull with { Voids = [[Ring(500)]] }).IsValid.Should().BeTrue();
        Validator.Validate(nearlyFull with { Voids = [[Ring(1_100)]] }).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldSaveTheVoids_AndTheAreaFromTheRingsMinusThem()
    {
        var chat = ChatWithOutline();
        _geometry.UnionArea(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Is<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>(v => v!.Count == 1))
            .Returns(13_000);
        SiteBoundaryCorrection? created = null;
        _corrections.When(c => c.Add(Arg.Any<SiteBoundaryCorrection>())).Do(call => created = call.Arg<SiteBoundaryCorrection>());

        var result = await CreateHandler().Handle(CommandFor(chat) with { Voids = [[VoidRing]] }, TestContext.Current.CancellationToken);

        created.Should().NotBeNull();
        created!.EditedVoids.Should().HaveCount(1);
        created.EditedVoids[0].Should().ContainSingle();
        created.EditedVoids[0][0][0].Should().Be(VoidRing[0]);
        created.EditedVoids[0][0][^1].Should().Be(VoidRing[0], "a void is stored closed, like the rings");
        created.AreaSquareMeters.Should().Be(13_000);
        result.ActiveBoundary.Voids.Should().HaveCount(1);
        result.ActiveBoundary.AreaSquareMeters.Should().Be(13_000);
    }

    [Fact]
    public async Task Handle_WithoutVoids_ShouldSaveAsBefore()
    {
        var chat = ChatWithOutline();
        SiteBoundaryCorrection? created = null;
        _corrections.When(c => c.Add(Arg.Any<SiteBoundaryCorrection>())).Do(call => created = call.Arg<SiteBoundaryCorrection>());

        await CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        created!.EditedVoids.Should().BeEmpty();
        created.AreaSquareMeters.Should().Be(14_321.5);
    }

    [Theory]
    [InlineData(RingValidationResult.VoidOutsidePart, "voidOutsidePart")]
    [InlineData(RingValidationResult.VoidsTouch, "voidsTouch")]
    [InlineData(RingValidationResult.SelfCrossing, "selfCrossing")]
    [InlineData(RingValidationResult.Degenerate, "degenerate")]
    [InlineData(RingValidationResult.DuplicateCorner, "duplicateCorner")]
    public async Task Handle_ShouldRefuseAnInvalidVoid_WithItsRingAndVoidIndex_AndSaveNothing(RingValidationResult result, string reason)
    {
        var chat = ChatWithOutline();
        _geometry.ValidateVoids(Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>())
            .Returns(new VoidValidation(result, 1));

        var act = () => CreateHandler().Handle(CommandFor(chat) with { Voids = [[VoidRing, VoidRing]] }, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.Reason.Should().Be(reason);
        thrown.Which.RingIndex.Should().Be(0);
        thrown.Which.VoidIndex.Should().Be(1);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_ShouldRejectARevisionThatIsNotAnIdentifier() =>
        Validator.Validate(Valid(revision: "not-a-guid")).IsValid.Should().BeFalse();

    // ---- handler ---------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateTheCorrectionLinkTheChatAndAppendOneLine_InOneSave()
    {
        var chat = ChatWithOutline();
        SiteBoundaryCorrection? added = null;
        _corrections.When(r => r.Add(Arg.Any<SiteBoundaryCorrection>())).Do(call => added = call.Arg<SiteBoundaryCorrection>());
        Domain.Chats.Message? line = null;
        _messages.When(r => r.Add(Arg.Any<Domain.Chats.Message>())).Do(call => line = call.Arg<Domain.Chats.Message>());

        var result = await CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        added.Should().NotBeNull();
        added!.UserId.Should().Be(Owner);
        added.AreaSquareMeters.Should().Be(14_321.5);
        added.FoundSnapshot.AreaSquareMeters.Should().Be(15_000);
        chat.ActiveBoundary!.CorrectionId.Should().Be(added.Id);
        chat.ActiveBoundary.Polygon.Should().BeEquivalentTo(FoundRing, "the stored outline keeps what Lucy found");

        line.Should().NotBeNull();
        line!.Role.Should().Be(MessageRole.Assistant);
        line.Kind.Should().Be(MessageKind.Text);
        line.Content.Should().Be("You edited the outline of Muscat Grand Mall — now 14,322 m².");

        result.ActiveBoundary.IsHandEdited.Should().BeTrue();
        result.ActiveBoundary.Source.Should().Be("UserCorrected");
        result.ActiveBoundary.Revision.Should().Be(added.Revision.ToString());
        result.Message.Content.Should().Be(line.Content);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUpdateTheLinkedCorrection_OnASecondEdit()
    {
        var chat = ChatWithOutline();
        var correction = SiteBoundaryCorrection.Create(
            Owner, "Muscat Grand Mall", 25.1555, 55.2215,
            new FoundSiteBoundarySnapshot(FoundRing, [], null, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
                SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [FoundRing], 15_000, [], Owner);
        chat.LinkSiteBoundaryCorrection(correction.Id, Owner);
        _corrections.GetByIdAsync(correction.Id, Owner, Arg.Any<CancellationToken>()).Returns(correction);
        var before = correction.Revision;

        var result = await CreateHandler().Handle(
            new SaveSiteBoundaryEditCommand(chat.Id, correction.Revision.ToString(), [EditedRing]),
            TestContext.Current.CancellationToken);

        correction.Revision.Should().NotBe(before);
        correction.EditedRings[0].Should().BeEquivalentTo(EditedRing);
        _corrections.DidNotReceive().Add(Arg.Any<SiteBoundaryCorrection>());
        result.ActiveBoundary.Revision.Should().Be(correction.Revision.ToString());
    }

    [Fact]
    public async Task Handle_ShouldStoreARingClosed_WhenTheClientSentItOpen()
    {
        var chat = ChatWithOutline();
        SiteBoundaryCorrection? added = null;
        _corrections.When(r => r.Add(Arg.Any<SiteBoundaryCorrection>())).Do(call => added = call.Arg<SiteBoundaryCorrection>());
        IReadOnlyList<GeoPoint> open = [.. EditedRing.Take(4)];

        await CreateHandler().Handle(
            new SaveSiteBoundaryEditCommand(chat.Id, chat.ActiveBoundary!.Revision.ToString(), [open]),
            TestContext.Current.CancellationToken);

        added!.EditedRings[0].Should().HaveCount(5);
        added.EditedRings[0][0].Should().Be(added.EditedRings[0][^1]);
    }

    [Fact]
    public async Task Handle_ShouldTurnANonOwnerAwayWithNotFound_AndAuditIt()
    {
        var chat = ChatWithOutline("someone-else");

        var act = () => CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _audit.Received(1).Add(Arg.Is<RoleAuditLog>(e => e!.Action == RoleAuditAction.AuthorizationDenied && e.ActorUserId == Owner));
        _corrections.DidNotReceive().Add(Arg.Any<SiteBoundaryCorrection>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTheChatHasNoOutline()
    {
        var chat = UserChat.Create("Chat", Owner, null, Owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        var act = () => CreateHandler().Handle(new SaveSiteBoundaryEditCommand(chat.Id, Guid.NewGuid().ToString(), [EditedRing]), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowAConflictCarryingTheCurrentRevision_WhenTheRevisionIsStale()
    {
        var chat = ChatWithOutline();

        var act = () => CreateHandler().Handle(CommandFor(chat, Guid.NewGuid().ToString()), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        thrown.Which.CurrentRevision.Should().Be(chat.ActiveBoundary!.Revision.ToString());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The editor can add a circle as a ring of its own or split a ring, so the ring count may differ from what was stored.</summary>
    [Fact]
    public async Task Handle_ShouldAccept_ADifferentNumberOfRingsThanTheOutlineInForce()
    {
        var chat = ChatWithOutline();
        SiteBoundaryCorrection? added = null;
        _corrections.When(r => r.Add(Arg.Any<SiteBoundaryCorrection>())).Do(call => added = call.Arg<SiteBoundaryCorrection>());

        var result = await CreateHandler().Handle(
            new SaveSiteBoundaryEditCommand(chat.Id, chat.ActiveBoundary!.Revision.ToString(), [EditedRing, EditedRing]),
            TestContext.Current.CancellationToken);

        added!.EditedRings.Should().HaveCount(2);
        result.ActiveBoundary.AdditionalPolygons.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(RingValidationResult.SelfCrossing, "selfCrossing")]
    [InlineData(RingValidationResult.Degenerate, "degenerate")]
    [InlineData(RingValidationResult.DuplicateCorner, "duplicateCorner")]
    public async Task Handle_ShouldRejectAnInvalidRing_WithItsIndexAndReason(RingValidationResult result, string reason)
    {
        var chat = ChatWithOutline();
        _geometry.Validate(Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(result);

        var act = () => CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.RingIndex.Should().Be(0);
        thrown.Which.Reason.Should().Be(reason);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectARingThatDriftedAwayFromWhatLucyFound()
    {
        var chat = ChatWithOutline();
        _geometry.Intersects(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), 25)
            .Returns(false);

        var act = () => CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.Reason.Should().Be("driftedAway");
    }

    [Fact]
    public async Task Handle_ShouldRejectAnOutlineMoreThanThreeTimesWhatLucyFound()
    {
        var chat = ChatWithOutline();
        _geometry.UnionArea(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>()).Returns(45_001);

        var act = () => CreateHandler().Handle(CommandFor(chat), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.Reason.Should().Be("tooLarge");
    }
}
