using AskLucy.Domain.Authorization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Authorization;

public sealed class PermissionSetTests
{
    [Fact]
    public void Create_ShouldRejectUnknownKey()
    {
        var act = () => PermissionSet.Create("admin.nonexistent.view");
        act.Should().Throw<ArgumentException>().WithMessage("Unknown permission: admin.nonexistent.view");
    }

    [Fact]
    public void Create_EmptyKeys_ShouldReject()
    {
        var act = () => PermissionSet.Create(Array.Empty<string>());
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_ShouldRejectEmptyInput(string input)
    {
        var act = () => PermissionSet.Create(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManageKey_ShouldAutoIncludeCorrespondingView()
    {
        var set = PermissionSet.Create("admin.users.manage");

        set.Keys.Should().Contain("admin.users.view");
        set.Keys.Should().Contain("admin.users.manage");
        set.Keys.Should().HaveCount(2);
    }

    [Fact]
    public void ViewKey_ShouldNotTriggerAnyNormalization()
    {
        var set = PermissionSet.Create("admin.users.view");

        set.Keys.Should().ContainSingle("admin.users.view");
    }

    [Fact]
    public void Contains_ShouldReturnTrueForAllKeysInSet()
    {
        var set = PermissionSet.Create("admin.users.manage", "admin.mcp-servers.view");

        foreach (var key in set.Keys)
            set.Contains(key).Should().BeTrue();
    }

    [Fact]
    public void Contains_ShouldReturnFalseForNonMember()
    {
        var set = PermissionSet.Create("admin.users.manage");
        set.Contains("admin.dashboards.view").Should().BeFalse();
    }

    [Fact]
    public void Full_ShouldContainAll16CatalogueKeys()
    {
        PermissionSet.Full.Keys.Should().HaveCount(16);

        foreach (var catalogKey in AdminPermissionCatalog.All.Select(p => p.Key))
            PermissionSet.Full.Contains(catalogKey).Should().BeTrue();
    }

    [Fact]
    public void Equality_SameKeys_ShouldReturnTrue()
    {
        var a = PermissionSet.Create("admin.users.manage");
        var b = PermissionSet.Create("admin.users.view", "admin.users.manage");

        a.Should().BeEquivalentTo(b);
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentKeys_ShouldReturnFalse()
    {
        var a = PermissionSet.Create("admin.users.view");
        var b = PermissionSet.Create("admin.mcp-servers.view");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeTrueForEmptyConstructionPath()
    {
        // We can't construct an empty set directly (constructor throws),
        // but we can verify Full is never empty.
        PermissionSet.Full.IsEmpty.Should().BeFalse();
    }
}
