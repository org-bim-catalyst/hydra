using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Roles.Commands.DuplicateRole;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class DuplicateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly DuplicateRoleCommandHandler _handler;

    private static readonly RoleRecord AdministratorRole = new(
        "admin-id", PrivilegedRoleNames.Administrator, null, true, BuiltInRolePermissions.Administrator([]), 1, null, "s");

    private static readonly RoleRecord UserRole = new("user-id", DefaultRole.Name, null, true, PermissionSet.Empty, 9, null, "s");

    public DuplicateRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _roleRepository.GetByIdAsync("admin-id", Arg.Any<CancellationToken>()).Returns(AdministratorRole);
        _roleRepository.GetByIdAsync("user-id", Arg.Any<CancellationToken>()).Returns(UserRole);
        _roleRepository.DuplicateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PermissionSet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RoleRecord("new-id", ci.ArgAt<string>(1), ci.ArgAt<string?>(2), false, ci.ArgAt<PermissionSet>(3), 0, null, "n"));

        _handler = new DuplicateRoleCommandHandler(_roleRepository, _currentUser);
    }

    [Fact]
    public async Task CopiesABuiltInRolesEffectivePermissions_AsANewCustomRole()
    {
        var result = await _handler.Handle(
            new DuplicateRoleCommand("admin-id", "  Deputy Admin ", "Almost an admin"), TestContext.Current.CancellationToken);

        result.IsBuiltIn.Should().BeFalse();
        result.Name.Should().Be("Deputy Admin");
        result.PermissionKeys.Should().BeEquivalentTo(AdministratorRole.Permissions.Keys);
        await _roleRepository.Received(1).DuplicateAsync(
            PrivilegedRoleNames.Administrator, "Deputy Admin", "Almost an admin", AdministratorRole.Permissions, "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesAnAdministrator()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        var act = () => _handler.Handle(new DuplicateRoleCommand("admin-id", "Copy", null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<SuperUserRequiredException>()).WithMessage(DuplicateRoleCommandHandler.RefusalMessage);
        await _roleRepository.DidNotReceiveWithAnyArgs().DuplicateAsync(default!, default!, default, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefusesARoleWithNothingToCopy()
    {
        var act = () => _handler.Handle(new DuplicateRoleCommand("user-id", "Copy of User", null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Throws_WhenTheSourceIsMissing()
    {
        var act = () => _handler.Handle(new DuplicateRoleCommand("ghost", "Copy", null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
