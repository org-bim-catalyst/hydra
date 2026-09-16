using AskLucy.Domain.Authorization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Authorization;

public sealed class AdminPermissionCatalogTests
{
    [Fact]
    public void All_ShouldContainExactly16Permissions()
    {
        AdminPermissionCatalog.All.Should().HaveCount(16);
    }

    [Fact]
    public void All_ShouldHaveUniqueKeys()
    {
        var keys = AdminPermissionCatalog.All.Select(p => p.Key).ToList();
        keys.Distinct().Should().HaveCount(16, "all permission keys must be unique");
    }

    [Theory]
    [InlineData("admin.dashboard.view")]
    [InlineData("admin.users.view")]
    [InlineData("admin.users.manage")]
    [InlineData("admin.ai-providers.view")]
    [InlineData("admin.ai-providers.manage")]
    [InlineData("admin.default-models.view")]
    [InlineData("admin.default-models.manage")]
    [InlineData("admin.ai-capabilities.view")]
    [InlineData("admin.ai-capabilities.manage")]
    [InlineData("admin.agent-policies.view")]
    [InlineData("admin.agent-policies.manage")]
    [InlineData("admin.system-agents.view")]
    [InlineData("admin.workflow-policies.view")]
    [InlineData("admin.workflow-policies.manage")]
    [InlineData("admin.mcp-servers.view")]
    [InlineData("admin.mcp-servers.manage")]
    public void TryGet_ShouldReturnTrueForAllCatalogueKeys(string key)
    {
        var result = AdminPermissionCatalog.TryGet(key, out var permission);
        result.Should().BeTrue($"{key} should be in the catalogue");
        permission.Should().NotBeNull();
        permission!.Key.Should().Be(key);
    }

    [Fact]
    public void TryGet_ShouldReturnFalseForUnknownKey()
    {
        var result = AdminPermissionCatalog.TryGet("admin.nonexistent.view", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void ManagePermission_ShouldHaveImpliesViewSet()
    {
        foreach (var p in AdminPermissionCatalog.All)
        {
            if (p.Level == AdminPermissionLevel.Manage)
                p.Implies.Should().NotBeNull($"{p.Key} should imply its View counterpart");

            if (p.Level == AdminPermissionLevel.View)
                p.Implies.Should().BeNull($"{p.Key} should not have implies set");
        }
    }

    [Fact]
    public void ManageImpliedKey_ShouldMatchCorrespondingView()
    {
        var manageKeys = AdminPermissionCatalog.All
            .Where(p => p.Level == AdminPermissionLevel.Manage)
            .ToList();

        foreach (var manage in manageKeys)
        {
            var viewKey = $"{manage.Key.Substring(0, manage.Key.LastIndexOf('.'))}.view";
            manage.Implies!.Key.Should().Be(viewKey);
        }
    }

    [Theory]
    [InlineData(AdminArea.Dashboard, 1)]
    [InlineData(AdminArea.Users, 2)]
    [InlineData(AdminArea.AiProviders, 2)]
    [InlineData(AdminArea.DefaultModels, 2)]
    [InlineData(AdminArea.AiCapabilities, 2)]
    [InlineData(AdminArea.AgentPolicies, 2)]
    [InlineData(AdminArea.SystemAgents, 1)]
    [InlineData(AdminArea.WorkflowPolicies, 2)]
    [InlineData(AdminArea.McpServers, 2)]
    public void ByArea_ShouldReturnCorrectCount(AdminArea area, int expected)
    {
        AdminPermissionCatalog.ByArea(area).Should().HaveCount(expected);
    }

    [Fact]
    public void All_KeysShouldFollowAdminKebebabPattern()
    {
        foreach (var p in AdminPermissionCatalog.All)
        {
            p.Key.Should().StartWith("admin.");
            // Verify area mapping is correct.
            var expectedAreaPrefix = $"admin.{GetExpectedAreaSlug(p.Area)}";
            p.Key.Should().StartWith(expectedAreaPrefix);
        }
    }

    private static string GetExpectedAreaSlug(AdminArea area) => area switch
    {
        AdminArea.Dashboard       => "dashboard",
        AdminArea.Users           => "users",
        AdminArea.AiProviders     => "ai-providers",
        AdminArea.DefaultModels   => "default-models",
        AdminArea.AiCapabilities  => "ai-capabilities",
        AdminArea.AgentPolicies   => "agent-policies",
        AdminArea.SystemAgents    => "system-agents",
        AdminArea.WorkflowPolicies=> "workflow-policies",
        AdminArea.McpServers      => "mcp-servers",
        _                         => throw new System.ArgumentOutOfRangeException(nameof(area))
    };
}
