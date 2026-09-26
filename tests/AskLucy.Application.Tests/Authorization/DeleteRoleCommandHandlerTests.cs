using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Commands.DeleteRole;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class DeleteRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly DeleteRoleCommandHandler _handler;

    public DeleteRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        // PermissionSet.Full carries View user content, which only a Super User may remove (specs/074 FR-016g).
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _handler = new DeleteRoleCommandHandler(_roleRepository, _currentUser, _cacheInvalidator);
    }

    [Fact]
    public async Task Handle_NoHolders_ReturnsZeroAndDeletes()
    {
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, PermissionSet.Full, 0, null, "stamp-1"));
        _roleRepository.DeleteAsync("role-1", "stamp-1", "actor-1", Arg.Any<CancellationToken>()).Returns(new List<string>());

        var result = await _handler.Handle(new DeleteRoleCommand("role-1", "stamp-1"), CancellationToken.None);

        result.UnassignedUserCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithHolders_ReportsCountAndEvictsEachCache()
    {
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, PermissionSet.Full, 3, null, "stamp-1"));
        _roleRepository.DeleteAsync("role-1", "stamp-1", "actor-1", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "user-1", "user-2", "user-3" });

        var result = await _handler.Handle(new DeleteRoleCommand("role-1", "stamp-1"), CancellationToken.None);

        result.UnassignedUserCount.Should().Be(3);
        _cacheInvalidator.Received(1).Evict("user-1");
        _cacheInvalidator.Received(1).Evict("user-2");
        _cacheInvalidator.Received(1).Evict("user-3");
    }

    [Fact]
    public async Task Handle_BuiltInRole_ThrowsUnauthorized()
    {
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Super User", null, true, PermissionSet.Full, 1, null, "stamp-1"));

        var act = () => _handler.Handle(new DeleteRoleCommand("role-1", "stamp-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_StaleConcurrencyStamp_ThrowsKeyNotFound()
    {
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, PermissionSet.Full, 0, null, "stamp-1"));
        _roleRepository.DeleteAsync("role-1", "stale", "actor-1", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>?)null);

        var act = () => _handler.Handle(new DeleteRoleCommand("role-1", "stale"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
