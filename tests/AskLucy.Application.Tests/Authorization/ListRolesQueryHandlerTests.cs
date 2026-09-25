using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Queries.GetRole;
using AskLucy.Application.Authorization.Roles.Queries.ListRoles;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class ListRolesQueryHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();

    [Fact]
    public async Task Handle_ReturnsPagedRoles_BuiltInsFirstPerRepositoryOrdering()
    {
        var handler = new ListRolesQueryHandler(_roleRepository);
        var items = new List<RoleRecord>
        {
            new("su", "Super User", null, true, PermissionSet.Full, 1, null, "s1"),
            new("role-1", "Moderator", null, false, PermissionSet.Create("admin.dashboard.view"), 2, null, "s2"),
        };
        _roleRepository.SearchAsync(null, 1, 20, Arg.Any<CancellationToken>()).Returns((items, 2));

        var result = await handler.Handle(new ListRolesQuery(null), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items[0].IsBuiltIn.Should().BeTrue();
        result.Items[0].PermissionKeys.Should().HaveCount(PermissionSet.Full.Keys.Count);
    }

    [Fact]
    public async Task Handle_PassesSearchThrough()
    {
        var handler = new ListRolesQueryHandler(_roleRepository);
        _roleRepository.SearchAsync("mod", 1, 20, Arg.Any<CancellationToken>()).Returns((new List<RoleRecord>(), 0));

        await handler.Handle(new ListRolesQuery("mod"), CancellationToken.None);

        await _roleRepository.Received(1).SearchAsync("mod", 1, 20, Arg.Any<CancellationToken>());
    }
}

public sealed class GetRoleQueryHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();

    [Fact]
    public async Task Handle_ExistingRole_ReturnsDto()
    {
        var handler = new GetRoleQueryHandler(_roleRepository);
        _roleRepository.GetByIdAsync("role-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, PermissionSet.Create("admin.dashboard.view"), 0, null, "s1"));

        var result = await handler.Handle(new GetRoleQuery("role-1"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Moderator");
    }

    [Fact]
    public async Task Handle_MissingRole_ReturnsNull()
    {
        var handler = new GetRoleQueryHandler(_roleRepository);
        _roleRepository.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var result = await handler.Handle(new GetRoleQuery("missing"), CancellationToken.None);

        result.Should().BeNull();
    }
}
