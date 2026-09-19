using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Commands.BulkLockUsers;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class BulkLockUsersCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly BulkLockUsersCommandHandler _handler;

    public BulkLockUsersCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _identityService.GetRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new BulkLockUsersCommandHandler(
            _mediator, _identityService, _userAdminRepository, _currentUser,
            Substitute.For<ILogger<BulkLockUsersCommandHandler>>());
    }

    private static UserAdminDto MakeUser(string id, bool isLockedOut = false) =>
        new(id, $"{id}@example.com", null, null, true, false, false, isLockedOut, "Regular", DateTime.UtcNow);

    [Fact]
    public async Task Handle_LocksEachResolvedId()
    {
        _userAdminRepository.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(MakeUser("user-1"));
        _userAdminRepository.GetByIdAsync("user-2", Arg.Any<CancellationToken>()).Returns(MakeUser("user-2"));

        var outcome = await _handler.Handle(
            new BulkLockUsersCommand(new BulkTarget(["user-1", "user-2"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(2);
        outcome.Skipped.Should().BeEmpty();
        await _identityService.Received(1).SetLockoutAsync("user-1", true, Arg.Any<CancellationToken>());
        await _identityService.Received(1).SetLockoutAsync("user-2", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsSelfIfPresent()
    {
        var outcome = await _handler.Handle(
            new BulkLockUsersCommand(new BulkTarget(["actor-1"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(0);
        outcome.Skipped.Should().ContainSingle(s => s.Id == "actor-1" && s.Reason == "Self");
    }

    [Fact]
    public async Task Handle_SkipsWithReason_RatherThanAborting_WhenTargetWouldStrandLastSuperUser()
    {
        _userAdminRepository.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(MakeUser("user-1"));
        _userAdminRepository.GetByIdAsync("super-1", Arg.Any<CancellationToken>()).Returns(MakeUser("super-1"));
        _identityService.GetRolesAsync("super-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var outcome = await _handler.Handle(
            new BulkLockUsersCommand(new BulkTarget(["user-1", "super-1"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
        outcome.Skipped.Should().ContainSingle(s => s.Id == "super-1" && s.Reason == "LastSuperUser");
        await _identityService.Received(1).SetLockoutAsync("user-1", true, Arg.Any<CancellationToken>());
        await _identityService.DidNotReceive().SetLockoutAsync("super-1", Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllMatching_ReResolvesIdsAtExecutionTime()
    {
        _mediator.Send(Arg.Is<GetUsersEligibleIdsQuery>(q => q != null && q.Action == UserBulkAction.Lock && q.Search == "ana"), Arg.Any<CancellationToken>())
            .Returns(["user-9"]);
        _userAdminRepository.GetByIdAsync("user-9", Arg.Any<CancellationToken>()).Returns(MakeUser("user-9"));

        var outcome = await _handler.Handle(
            new BulkLockUsersCommand(new BulkTarget(null, true), "ana"), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
        await _identityService.Received(1).SetLockoutAsync("user-9", true, Arg.Any<CancellationToken>());
    }
}
