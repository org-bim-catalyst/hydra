using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Commands.BulkUnlockUsers;
using AskLucy.Application.Users.Queries.GetUsersEligibleIds;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class BulkUnlockUsersCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly BulkUnlockUsersCommandHandler _handler;

    public BulkUnlockUsersCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new BulkUnlockUsersCommandHandler(
            _mediator, _identityService, _userAdminRepository, _currentUser,
            Substitute.For<ILogger<BulkUnlockUsersCommandHandler>>());
    }

    private static UserAdminDto MakeUser(string id, bool isLockedOut = true) =>
        new(id, $"{id}@example.com", null, null, true, false, true, isLockedOut, "Regular", DateTime.UtcNow);

    [Fact]
    public async Task Handle_UnlocksEachResolvedId_NoLastSuperUserGuard()
    {
        _userAdminRepository.GetByIdAsync("super-1", Arg.Any<CancellationToken>()).Returns(MakeUser("super-1"));

        var outcome = await _handler.Handle(
            new BulkUnlockUsersCommand(new BulkTarget(["super-1"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
        outcome.Skipped.Should().BeEmpty();
        await _identityService.Received(1).SetLockoutAsync("super-1", false, Arg.Any<CancellationToken>());
        await _identityService.DidNotReceiveWithAnyArgs().GetRolesAsync(default!, default);
    }

    [Fact]
    public async Task Handle_SkipsNotFound()
    {
        _userAdminRepository.GetByIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((UserAdminDto?)null);

        var outcome = await _handler.Handle(
            new BulkUnlockUsersCommand(new BulkTarget(["ghost"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(0);
        outcome.Skipped.Should().ContainSingle(s => s.Id == "ghost" && s.Reason == "NotFound");
    }

    [Fact]
    public async Task Handle_AllMatching_ReResolvesViaEligibleIdsQuery()
    {
        _mediator.Send(Arg.Is<GetUsersEligibleIdsQuery>(q => q.Action == UserBulkAction.Unlock), Arg.Any<CancellationToken>())
            .Returns(["user-9"]);
        _userAdminRepository.GetByIdAsync("user-9", Arg.Any<CancellationToken>()).Returns(MakeUser("user-9"));

        var outcome = await _handler.Handle(
            new BulkUnlockUsersCommand(new BulkTarget(null, true), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
    }
}
