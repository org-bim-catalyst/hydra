using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

/// <summary>
/// Direct unit tests for the shared FR-023 guard (data-model.md &#167; Commands) — the guard
/// itself, isolated from any specific handler; each handler's own tests
/// (Lock/ChangeUserRole/DeleteUser) additionally exercise it through their real call sites.
/// </summary>
public sealed class LastSuperUserGuardTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();

    [Fact]
    public async Task ShouldNoOp_WhenActionDoesNotRemoveSuperUserStatus()
    {
        var act = () => LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            _identityService, "target-1", actionRemovesTargetsSuperUserStatus: false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _identityService.DidNotReceiveWithAnyArgs().GetRolesAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldNoOp_WhenTargetIsNotCurrentlyASuperUser()
    {
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Administrator"]);

        var act = () => LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            _identityService, "target-1", actionRemovesTargetsSuperUserStatus: true, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _identityService.DidNotReceiveWithAnyArgs().CountActiveSuperUsersAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldThrow_WhenTargetIsTheOnlyActiveSuperUser()
    {
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var act = () => LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            _identityService, "target-1", actionRemovesTargetsSuperUserStatus: true, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ShouldNotThrow_WhenTargetIsASuperUserButAnotherActiveSuperUserExists()
    {
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(2);

        var act = () => LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            _identityService, "target-1", actionRemovesTargetsSuperUserStatus: true, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
