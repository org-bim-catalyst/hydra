using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Commands.UpdateRole;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class UpdateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _roleAssignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly UpdateRoleCommandHandler _handler;

    public UpdateRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new UpdateRoleCommandHandler(_roleRepository, _roleAssignmentRepository, _currentUser, _cacheInvalidator);
    }

    [Fact]
    public async Task Handle_ValidUpdate_EvictsEveryCurrentHoldersCache()
    {
        var permissions = PermissionSet.Create("admin.mcp-servers.view");
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, permissions, 2, null, "stamp-1"));
        _roleRepository.UpdateAsync("role-1", "Mod", "desc", Arg.Any<PermissionSet>(), "stamp-1", "actor-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Mod", "desc", false, permissions, 2, null, "stamp-2"));
        _roleAssignmentRepository.ListUserIdsByRoleAsync("role-1", Arg.Any<CancellationToken>()).Returns(["user-1", "user-2"]);

        var result = await _handler.Handle(new UpdateRoleCommand("role-1", "Mod", "desc", ["admin.mcp-servers.view"], "stamp-1"), CancellationToken.None);

        result.ConcurrencyStamp.Should().Be("stamp-2");
        _cacheInvalidator.Received(1).Evict("user-1");
        _cacheInvalidator.Received(1).Evict("user-2");
    }

    [Fact]
    public async Task Handle_BuiltInRole_ThrowsUnauthorized()
    {
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Administrator", null, true, PermissionSet.Full, 1, null, "stamp-1"));

        var act = () => _handler.Handle(new UpdateRoleCommand("role-1", "Administrator", null, ["admin.dashboard.view"], "stamp-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_RoleNotFound_ThrowsKeyNotFound()
    {
        _roleRepository.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new UpdateRoleCommand("missing", "Mod", null, ["admin.dashboard.view"], "stamp-1"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_StaleConcurrencyStamp_PropagatesNullAsKeyNotFound()
    {
        var permissions = PermissionSet.Create("admin.dashboard.view");
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, permissions, 0, null, "stamp-1"));
        _roleRepository.UpdateAsync("role-1", "Moderator", null, Arg.Any<PermissionSet>(), "stale-stamp", "actor-1", Arg.Any<CancellationToken>())
            .Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new UpdateRoleCommand("role-1", "Moderator", null, ["admin.dashboard.view"], "stale-stamp"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
