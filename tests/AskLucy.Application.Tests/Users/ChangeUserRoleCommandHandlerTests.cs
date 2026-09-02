using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.ChangeUserRole;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

/// <summary>FR-014 privilege-escalation guard and FR-023 last-Super-User guard (specs/001-admin-dashboard).</summary>
public sealed class ChangeUserRoleCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ChangeUserRoleCommandHandler _handler;

    public ChangeUserRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(5);
        _handler = new ChangeUserRoleCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<ChangeUserRoleCommandHandler>>());
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToGrantSuperUser()
    {
        _currentUser.IsInRole("Super User").Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Super User"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identityService.DidNotReceiveWithAnyArgs().ChangeRoleAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToGrantAdministrator()
    {
        _currentUser.IsInRole("Super User").Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Administrator"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToRevokeATargetsSuperUserRole()
    {
        _currentUser.IsInRole("Super User").Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Regular"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ShouldAllow_WhenPlainAdministratorChangesARegularUsersRole()
    {
        _currentUser.IsInRole("Super User").Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Regular"), CancellationToken.None);

        await _identityService.Received(1).ChangeRoleAsync("target-1", "Regular", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldAllow_WhenSuperUserGrantsSuperUser()
    {
        _currentUser.IsInRole("Super User").Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Super User"), CancellationToken.None);

        await _identityService.Received(1).ChangeRoleAsync("target-1", "Super User", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldReject_WhenDemotingTheLastActiveSuperUser()
    {
        _currentUser.IsInRole("Super User").Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Regular"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identityService.DidNotReceiveWithAnyArgs().ChangeRoleAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldAllow_WhenTargetIsTheLastActiveSuperUserButRoleStaysSuperUser()
    {
        // Reassigning the same "Super User" role to the last Super User isn't a removal.
        _currentUser.IsInRole("Super User").Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Super User"), CancellationToken.None);

        await _identityService.Received(1).ChangeRoleAsync("target-1", "Super User", Arg.Any<CancellationToken>());
    }
}
