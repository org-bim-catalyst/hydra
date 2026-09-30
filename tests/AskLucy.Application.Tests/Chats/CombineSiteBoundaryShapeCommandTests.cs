using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.CombineSiteBoundaryShape;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>specs/079 - adding a circle to the outline being edited, or cutting one out: bounds, ownership and the refusals.</summary>
public sealed class CombineSiteBoundaryShapeCommandTests
{
    private const string Owner = "owner-1";

    private static readonly IReadOnlyList<GeoPoint> Ring =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1550, 55.2210)];

    private static readonly GeoPoint Centre = new(25.1555, 55.2215);

    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly ISiteRingGeometry _geometry = Substitute.For<ISiteRingGeometry>();
    private readonly IRoleAuditLogRepository _audit = Substitute.For<IRoleAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public CombineSiteBoundaryShapeCommandTests()
    {
        _currentUser.UserId.Returns(Owner);
        _geometry.Validate(Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(RingValidationResult.Ok);
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<CombineOperation>())
            .Returns(new CombineResult(CombineFailure.None, [Ring]));
    }

    private CombineSiteBoundaryShapeCommandHandler Create() =>
        new(_chats, _geometry, new ChatOwnershipAuditor(_audit, _unitOfWork), _currentUser);

    private UserChat ChatWithOutline(string owner = Owner)
    {
        var chat = UserChat.Create("Chat", owner, null, owner);
        chat.SetActiveBoundary("Site", 25.1555, 55.2215, Ring, 15_000, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "x", owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        return chat;
    }

    private static CombineSiteBoundaryShapeCommand CommandFor(Guid chatId, CombineOperation operation = CombineOperation.Add) =>
        new(chatId, [Ring], operation, Centre, 30);

    // ---- validator -------------------------------------------------------

    private static readonly CombineSiteBoundaryShapeCommandValidator Validator = new();

    [Fact]
    public void Validator_AcceptsAWellFormedRequest() =>
        Validator.Validate(CommandFor(Guid.NewGuid())).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(5_000.5)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Validator_RejectsARadiusOutsideOneMetreToFiveKilometres(double radius) =>
        Validator.Validate(CommandFor(Guid.NewGuid()) with { RadiusMeters = radius }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(1)]
    [InlineData(5_000)]
    public void Validator_AcceptsTheRadiusLimits(double radius) =>
        Validator.Validate(CommandFor(Guid.NewGuid()) with { RadiusMeters = radius }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(91, 55)]
    [InlineData(25, 181)]
    [InlineData(double.NaN, 55)]
    public void Validator_RejectsACentreOffTheEarth(double latitude, double longitude) =>
        Validator.Validate(CommandFor(Guid.NewGuid()) with { Centre = new GeoPoint(latitude, longitude) }).IsValid.Should().BeFalse();

    [Fact]
    public void Validator_BoundsTheRings()
    {
        Validator.Validate(CommandFor(Guid.NewGuid()) with { Rings = [] }).IsValid.Should().BeFalse();
        Validator.Validate(CommandFor(Guid.NewGuid()) with { Rings = [.. Enumerable.Range(0, 21).Select(_ => Ring)] }).IsValid.Should().BeFalse();
        Validator.Validate(CommandFor(Guid.NewGuid()) with { Rings = [[Ring[0], Ring[1]]] }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsAnOperationThatIsNotAddOrCut() =>
        Validator.Validate(CommandFor(Guid.NewGuid()) with { Operation = (CombineOperation)9 }).IsValid.Should().BeFalse();

    // ---- handler ---------------------------------------------------------

    [Theory]
    [InlineData(CombineOperation.Add)]
    [InlineData(CombineOperation.Cut)]
    public async Task Handle_ReturnsTheRingsTheGeometryProduced_ForEitherOperation(CombineOperation operation)
    {
        var chat = ChatWithOutline();

        var result = await Create().Handle(CommandFor(chat.Id, operation), TestContext.Current.CancellationToken);

        result.Rings.Should().ContainSingle();
        _geometry.Received(1).Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), operation);
    }

    [Fact]
    public async Task Handle_ShapesTheCircleAroundTheCentre_AtTheRequestedRadius()
    {
        var chat = ChatWithOutline();
        IReadOnlyList<GeoPoint>? shape = null;
        _geometry.When(g => g.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<CombineOperation>()))
            .Do(call => shape = call.ArgAt<IReadOnlyList<GeoPoint>>(1));

        await Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        shape.Should().NotBeNull();
        shape!.Count.Should().BeGreaterThanOrEqualTo(24);
        GeometryMath.AreaSquareMeters(shape).Should().BeApproximately(Math.PI * 30 * 30, 60);
    }

    [Fact]
    public async Task Handle_SavesNothing()
    {
        var chat = ChatWithOutline();

        await Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TurnsANonOwnerAwayWithNotFound_AndAuditsIt()
    {
        var chat = ChatWithOutline("someone-else");

        var act = () => Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _audit.Received(1).Add(Arg.Is<RoleAuditLog>(e => e!.Action == RoleAuditAction.AuthorizationDenied && e.ActorUserId == Owner));
        _geometry.DidNotReceive().Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<CombineOperation>());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTheChatHasNoOutline()
    {
        var chat = UserChat.Create("Chat", Owner, null, Owner);
        _chats.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        var act = () => Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_RefusesAnInvalidInputRing_WithItsIndex()
    {
        var chat = ChatWithOutline();
        _geometry.Validate(Arg.Any<IReadOnlyList<GeoPoint>>()).Returns(RingValidationResult.SelfCrossing);

        var act = () => Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.RingIndex.Should().Be(0);
        thrown.Which.Reason.Should().Be("selfCrossing");
    }

    [Theory]
    [InlineData(CombineFailure.HoleNotSupported, "holeNotSupported", "hole")]
    [InlineData(CombineFailure.NothingLeft, "nothingLeft", "whole outline")]
    public async Task Handle_TurnsAnUnusableResultIntoARefusalTheUserCanAct_On(CombineFailure failure, string reason, string saysSo)
    {
        var chat = ChatWithOutline();
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<CombineOperation>())
            .Returns(new CombineResult(failure, []));

        var act = () => Create().Handle(CommandFor(chat.Id, CombineOperation.Cut), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.Reason.Should().Be(reason);
        thrown.Which.Message.Should().Contain(saysSo);
    }

    [Fact]
    public async Task Handle_RefusesAResultOfMoreThanTwentyRings()
    {
        var chat = ChatWithOutline();
        _geometry.Combine(Arg.Any<IReadOnlyList<IReadOnlyList<GeoPoint>>>(), Arg.Any<IReadOnlyList<GeoPoint>>(), Arg.Any<CombineOperation>())
            .Returns(new CombineResult(CombineFailure.None, [.. Enumerable.Range(0, 21).Select(_ => Ring)]));

        var act = () => Create().Handle(CommandFor(chat.Id), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<SiteBoundaryGeometryRejectedException>();
        thrown.Which.Reason.Should().Be("tooManyRings");
    }
}
