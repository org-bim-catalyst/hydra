using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
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
        serverDeclaredRiskLevel, "[]", version: "1");

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

    /// <summary>
    /// A routine refresh re-points the same row at the new snapshot — it never creates a second one,
    /// because NamespacedName is stable across discoveries and unique in the database.
    /// </summary>
    [Fact]
    public void RefreshFromSnapshot_ShouldCarryAnUnchangedToolsReviewForward()
    {
        var tool = DiscoverTool(serverDeclaredRiskLevel: AgentToolRiskLevel.Low);
        tool.Activate(Actor, effectiveRiskLevelOverride: AgentToolRiskLevel.Medium, requiredPermissionsJsonOverride: "[\"ReadExternalData\"]");
        var activatedAt = tool.ActivatedAtUtc;
        var newSnapshotId = Guid.NewGuid();

        tool.RefreshFromSnapshot(newSnapshotId, "Search v2", "New description.", "{\"x\":1}", "{}", null, "2", definitionChanged: false, Actor);

        tool.McpCapabilitySnapshotId.Should().Be(newSnapshotId);
        tool.DisplayName.Should().Be("Search v2");
        tool.InputSchemaJson.Should().Be("{\"x\":1}");
        tool.Version.Should().Be("2");
        tool.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
        tool.ActivatedByUserId.Should().Be(Actor);
        tool.ActivatedAtUtc.Should().Be(activatedAt);
        tool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Medium);
        tool.RequiredPermissionsJson.Should().Be("[\"ReadExternalData\"]");
    }

    /// <summary>
    /// FR-022's re-review rule: a changed definition invalidates the administrator's review, so the
    /// tool returns to PendingReview with the most restrictive classification and no permissions.
    /// </summary>
    [Fact]
    public void RefreshFromSnapshot_ShouldReturnToPendingReview_WhenTheDefinitionChanged()
    {
        var tool = DiscoverTool();
        tool.Activate(Actor, effectiveRiskLevelOverride: AgentToolRiskLevel.Low, requiredPermissionsJsonOverride: "[\"ExternalNetwork\"]");

        tool.RefreshFromSnapshot(Guid.NewGuid(), "Search", "Searches things.", "{\"x\":1}", "{}", null, "2", definitionChanged: true, Actor);

        tool.ActivationStatus.Should().Be(McpToolActivationStatus.PendingReview);
        tool.ActivatedByUserId.Should().BeNull();
        tool.ActivatedAtUtc.Should().BeNull();
        tool.EffectiveRiskLevel.Should().Be(AgentToolRiskLevel.Critical);
        tool.RequiredPermissionsJson.Should().Be("[]");
    }

    /// <summary>A tool that reappears after being marked removed must become usable again.</summary>
    [Fact]
    public void RefreshFromSnapshot_ShouldRestoreAvailability()
    {
        var tool = DiscoverTool();
        tool.MarkUnavailable();

        tool.RefreshFromSnapshot(Guid.NewGuid(), "Search", "Searches things.", "{}", "{}", null, "1", definitionChanged: false, Actor);

        tool.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void MarkUnavailable_ShouldSetIsAvailableFalse()
    {
        var tool = DiscoverTool();

        tool.MarkUnavailable();

        tool.IsAvailable.Should().BeFalse();
    }
}
