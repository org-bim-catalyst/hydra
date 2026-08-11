using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Mcp;

public sealed class McpToolTests
{
    private const string Actor = "admin-1";

    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid SnapshotId = Guid.NewGuid();

    private static McpTool DiscoverTool(AgentToolRiskLevel? serverDeclaredRiskLevel = null) => McpTool.CreateFromDiscovery(
        ServerId, SnapshotId, "search", "Search", "Searches things.", "{}", "{}", null,
        serverDeclaredRiskLevel, "[]", version: "1", carriedForwardActivation: null);

    [Fact]
    public void CreateFromDiscovery_ShouldStartPendingReview()
    {
        var tool = DiscoverTool();

        tool.ActivationStatus.Should().Be(McpToolActivationStatus.PendingReview);
    }

    [Fact]
    public void CreateFromDiscovery_ShouldDefaultEffectiveRiskLevelToCritical_WhenServerDeclaresNone()
    {
        var tool = DiscoverTool(serverDeclaredRiskLevel: null);

        tool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Critical);
    }

    [Fact]
    public void CreateFromDiscovery_ShouldStartPendingReview_EvenWhenServerDeclaresLowRisk()
    {
        var tool = DiscoverTool(serverDeclaredRiskLevel: AgentToolRiskLevel.Low);

        tool.ActivationStatus.Should().Be(McpToolActivationStatus.PendingReview);
        tool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Low);
    }

    [Fact]
    public void NamespacedName_ShouldBeServerAndToolQualified()
    {
        var tool = DiscoverTool();

        tool.NamespacedName.Should().Be($"mcp:{ServerId}:search");
    }

    [Fact]
    public void Activate_ThenDeactivate_ThenActivate_ShouldTransitionCorrectly()
    {
        var tool = DiscoverTool();

        tool.Activate(Actor, effectiveRiskLevelOverride: null, requiredPermissionsJsonOverride: null);
        tool.ActivationStatus.Should().Be(McpToolActivationStatus.Active);

        tool.Deactivate(Actor);
        tool.ActivationStatus.Should().Be(McpToolActivationStatus.Deactivated);

        tool.Activate(Actor, effectiveRiskLevelOverride: null, requiredPermissionsJsonOverride: null);
        tool.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
    }

    [Fact]
    public void Activate_ShouldApplyRiskLevelOverride()
    {
        var tool = DiscoverTool(serverDeclaredRiskLevel: AgentToolRiskLevel.Low);

        tool.Activate(Actor, effectiveRiskLevelOverride: AgentToolRiskLevel.High, requiredPermissionsJsonOverride: null);

        tool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.High);
    }

    [Fact]
    public void CreateFromDiscovery_ShouldCarryForwardActivation_WhenProvided()
    {
        var activatedAt = DateTime.UtcNow.AddDays(-1);
        var tool = McpTool.CreateFromDiscovery(
            ServerId, SnapshotId, "search", "Search", "desc", "{}", "{}", null, AgentToolRiskLevel.Low, "[]", "1",
            carriedForwardActivation: (McpToolActivationStatus.Active, Actor, activatedAt));

        tool.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
        tool.ActivatedByUserId.Should().Be(Actor);
        tool.ActivatedAtUtc.Should().Be(activatedAt);
    }

    [Fact]
    public void MarkUnavailable_ShouldSetIsAvailableFalse()
    {
        var tool = DiscoverTool();

        tool.MarkUnavailable();

        tool.IsAvailable.Should().BeFalse();
    }
}
