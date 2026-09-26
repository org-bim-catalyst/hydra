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

    private const string ContentView = AdminPermissionCatalog.OperationalFailuresContentView;

    [Fact]
    public async Task ResolveAsync_SuperUser_ReturnsFullCatalogueWithoutARoundTrip()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Should().Be(PermissionSet.Full);
        result.Contains(ContentView).Should().BeTrue();
        await _roleRepository.DidNotReceive().GetByNormalizedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_Administrator_WithoutStoredGrant_GetsEverythingExceptContentView()
    {
        // specs/074 research D14: the first exception to "built-in ⇒ full catalogue".
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Administrator"]);
        _roleRepository.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("admin", "Administrator", null, true, PermissionSet.Full.Except(AdminPermissionCatalog.SuperUserControlledKeys), 1, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Contains(ContentView).Should().BeFalse();
        result.Keys.Should().HaveCount(PermissionSet.Full.Keys.Count - 1);
        result.Contains("admin.operational-failures.view").Should().BeTrue();
        result.Contains("admin.operational-failures.manage").Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_Administrator_WithStoredGrant_GetsContentView()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Administrator"]);
        _roleRepository.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("admin", "Administrator", null, true, PermissionSet.Create(ContentView), 1, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Should().Be(PermissionSet.Full);
    }

    [Fact]
    public async Task ResolveAsync_Administrator_RoleRowMissing_StillGetsEverythingExceptContentView()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Administrator"]);
        _roleRepository.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Should().Be(PermissionSet.Full.Except(AdminPermissionCatalog.SuperUserControlledKeys));
    }

    [Fact]
    public async Task ResolveAsync_CustomRoleWithStoredContentView_HasIt()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns(["Investigator"]);
        _roleRepository.GetByNormalizedNameAsync("INVESTIGATOR", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-3", "Investigator", null, false,
                PermissionSet.Create("admin.operational-failures.view", ContentView), 1, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Contains(ContentView).Should().BeTrue();
        result.Contains("admin.operational-failures.view").Should().BeTrue();
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
    public async Task ResolveAsync_NoRoleRow_IsTreatedAsTheUserRole()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns([]);
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("user-id", DefaultRole.Name, null, true, PermissionSet.Create("admin.dashboard.view"), 0, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Keys.Should().BeEquivalentTo(["admin.dashboard.view"]);
    }

    [Fact]
    public async Task ResolveAsync_UserRole_WithNothingAdded_ReturnsEmpty()
    {
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns([DefaultRole.Name]);
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("user-id", DefaultRole.Name, null, true, PermissionSet.Empty, 0, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_UserRole_NeverHonoursAStoredContentViewGrant()
    {
        // Every account holds the User role: honouring this would expose every user's content to everyone.
        _identityService.GetRolesAsync("user-1", Arg.Any<CancellationToken>()).Returns([DefaultRole.Name]);
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("user-id", DefaultRole.Name, null, true, PermissionSet.Create(ContentView, "admin.dashboard.view"), 0, null, "s"));

        var result = await _sut.ResolveAsync("user-1", TestContext.Current.CancellationToken);

        result.Keys.Should().BeEquivalentTo(["admin.dashboard.view"]);
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
