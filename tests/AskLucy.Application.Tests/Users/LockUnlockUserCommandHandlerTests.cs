using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.LockUser;
using AskLucy.Application.Users.Commands.UnlockUser;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class LockUnlockUserCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public LockUnlockUserCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(5);
        _identityService.GetRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Lock_ShouldRejectSelfTargeting()
    {
        var handler = new LockUserCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<LockUserCommandHandler>>());

        var act = () => handler.Handle(new LockUserCommand("admin-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identityService.DidNotReceiveWithAnyArgs().SetLockoutAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Lock_ShouldCallSetLockoutAsync_WhenTargetingSomeoneElse()
    {
        var handler = new LockUserCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<LockUserCommandHandler>>());

        await handler.Handle(new LockUserCommand("user-2"), CancellationToken.None);

        await _identityService.Received(1).SetLockoutAsync("user-2", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_ShouldRejectRemovingTheLastActiveSuperUser()
    {
        _identityService.GetRolesAsync("user-2", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);
        var handler = new LockUserCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<LockUserCommandHandler>>());

        var act = () => handler.Handle(new LockUserCommand("user-2"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identityService.DidNotReceiveWithAnyArgs().SetLockoutAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unlock_ShouldCallSetLockoutAsync_EvenForSelf()
    {
        var handler = new UnlockUserCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<UnlockUserCommandHandler>>());

        await handler.Handle(new UnlockUserCommand("admin-1"), CancellationToken.None);

        await _identityService.Received(1).SetLockoutAsync("admin-1", false, Arg.Any<CancellationToken>());
    }
}
