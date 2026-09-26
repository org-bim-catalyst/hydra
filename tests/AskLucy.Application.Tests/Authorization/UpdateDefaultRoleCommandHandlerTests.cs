using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Roles.Commands.UpdateDefaultRole;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class UpdateDefaultRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _roleAssignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly UpdateDefaultRoleCommandHandler _handler;

    private static readonly RoleRecord UserRole = new("user-id", DefaultRole.Name, null, true, PermissionSet.Empty, 2, null, "stamp");

    public UpdateDefaultRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>()).Returns(UserRole);
        _roleRepository.UpdateDefaultRoleAsync(Arg.Any<string?>(), Arg.Any<PermissionSet>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => UserRole with { Description = ci.ArgAt<string?>(0), Permissions = ci.ArgAt<PermissionSet>(1) });
        _roleAssignmentRepository.ListUserIdsByRoleAsync("user-id", Arg.Any<CancellationToken>()).Returns(["u1", "u2"]);

        _handler = new UpdateDefaultRoleCommandHandler(_roleRepository, _roleAssignmentRepository, _currentUser, _cacheInvalidator);
    }

    [Fact]
    public async Task AddsPermissions_AndEvictsEveryHolder()
    {
        var result = await _handler.Handle(
            new UpdateDefaultRoleCommand("Everyone", ["admin.dashboard.view"], "stamp"), TestContext.Current.CancellationToken);

        result.IsDefault.Should().BeTrue();
        result.PermissionKeys.Should().BeEquivalentTo(["admin.dashboard.view"]);
        await _roleRepository.Received(1).UpdateDefaultRoleAsync(
            "Everyone", Arg.Is<PermissionSet>(p => p!.Contains("admin.dashboard.view")), "stamp", "actor-1", Arg.Any<CancellationToken>());
        _cacheInvalidator.Received(1).Evict("u1");
        _cacheInvalidator.Received(1).Evict("u2");
    }

    [Fact]
    public async Task AllowsNoAddedPermissions()
    {
        await _handler.Handle(new UpdateDefaultRoleCommand(null, [], "stamp"), TestContext.Current.CancellationToken);

        await _roleRepository.Received(1).UpdateDefaultRoleAsync(
            null, Arg.Is<PermissionSet>(p => p!.IsEmpty), "stamp", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesContentView_EvenForASuperUser()
    {
        _currentUser.IsInRole(Arg.Any<string>()).Returns(true);

        var act = () => _handler.Handle(
            new UpdateDefaultRoleCommand(null, [AdminPermissionCatalog.OperationalFailuresContentView], "stamp"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _roleRepository.DidNotReceiveWithAnyArgs().UpdateDefaultRoleAsync(default, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Throws_WhenTheUserRoleIsMissing()
    {
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new UpdateDefaultRoleCommand(null, [], "stamp"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Throws_WhenACustomRoleMerelyShadowsTheName()
    {
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>())
            .Returns(UserRole with { IsBuiltIn = false });

        var act = () => _handler.Handle(new UpdateDefaultRoleCommand(null, [], "stamp"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
