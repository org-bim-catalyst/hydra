using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.DeleteUser;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class DeleteUserCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly DeleteUserCommandHandler _handler;

    public DeleteUserCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(5);
        _identityService.GetRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _userAdminRepository.DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _handler = new DeleteUserCommandHandler(
            _identityService, _userAdminRepository, _currentUser, Substitute.For<ILogger<DeleteUserCommandHandler>>());
    }

    [Fact]
    public async Task ShouldRejectSelfTargeting()
    {
        var act = () => _handler.Handle(new DeleteUserCommand("admin-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _userAdminRepository.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldSetIsDeleted_ViaRepository_WhenTargetingSomeoneElse()
    {
        await _handler.Handle(new DeleteUserCommand("user-2"), CancellationToken.None);

        await _userAdminRepository.Received(1).DeleteAsync("user-2", "admin-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldRejectRemovingTheLastActiveSuperUser()
    {
        _identityService.GetRolesAsync("user-2", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var act = () => _handler.Handle(new DeleteUserCommand("user-2"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _userAdminRepository.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldThrowKeyNotFound_WhenTargetDoesNotExist()
    {
        _userAdminRepository.DeleteAsync("missing-user", "admin-1", Arg.Any<CancellationToken>()).Returns(false);

        var act = () => _handler.Handle(new DeleteUserCommand("missing-user"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
