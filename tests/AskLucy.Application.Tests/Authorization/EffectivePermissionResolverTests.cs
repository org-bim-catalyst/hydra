using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class EffectivePermissionResolverTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly EffectivePermissionResolver _sut;

    public EffectivePermissionResolverTests()
    {
        _sut = new EffectivePermissionResolver(_identityService, _roleRepository);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task ResolveAsync_BuiltInRole_ReturnsFullCatalogue(string roleName)
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns([roleName]);

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Should().Be(PermissionSet.Full);
        await _roleRepository.DidNotReceive().GetByNormalizedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_CustomRole_ReturnsStoredPermissionsIntersectedWithCatalogue()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Moderator"]);
        var storedPermissions = PermissionSet.Create("admin.mcp-servers.manage");
        _roleRepository.GetByNormalizedNameAsync("MODERATOR", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", null, false, storedPermissions, 1, null, "stamp-1"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Contains("admin.mcp-servers.manage").Should().BeTrue();
        result.Contains("admin.mcp-servers.view").Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_CustomRoleIsBuiltInFlagTrue_ReturnsFullCatalogueEvenIfStoredSetIsPartial()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Legacy Admin"]);
        _roleRepository.GetByNormalizedNameAsync("LEGACY ADMIN", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-2", "Legacy Admin", null, true, PermissionSet.Create("admin.dashboard.view"), 1, null, "stamp-2"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Should().Be(PermissionSet.Full);
    }

    [Fact]
    public async Task ResolveAsync_NoRole_ReturnsEmpty()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_RoleRowMissing_ReturnsEmpty()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Ghost"]);
        _roleRepository.GetByNormalizedNameAsync("GHOST", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.IsEmpty.Should().BeTrue();
    }
}
