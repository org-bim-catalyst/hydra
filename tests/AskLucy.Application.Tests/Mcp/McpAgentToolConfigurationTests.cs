using System.Text.Json;
using AskLucy.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// research.md Decision 3/6 — an agent's draft-time tool configuration
/// (<c>Agent.AddTool</c>/<c>AgentTool</c>) has no tool-name validation at all beyond
/// non-empty (confirmed by inspection: neither <c>UpdateAgentCommandValidator</c> nor
/// <c>AgentTool.Create</c> restricts format or length) — an MCP tool's namespaced
/// `mcp:{serverId}:{toolName}` string is accepted, stored, and round-trips through
/// <c>Agent.Publish</c>'s `ToolsSnapshotJson` identically to a native tool's plain identifier,
/// with zero schema change to spec 020's <c>AgentTool</c>/<c>AgentVersion</c> entities. The
/// resulting snapshot resolving through <c>AgentToolCatalog.Find</c> at execution time is proven
/// end-to-end by <see cref="McpToolExecutionOrchestratorIntegrationTests"/>.
/// </summary>
public sealed class McpAgentToolConfigurationTests
{
    private const string OwnerId = "user-1";

    [Fact]
    public void AddTool_ShouldAcceptANamespacedMcpToolName_AndRoundTripThroughPublishedToolsSnapshotJson()
    {
        var namespacedName = $"mcp:{Guid.NewGuid()}:search";
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

        agent.AddTool(namespacedName, null, OwnerId);
        var version = agent.Publish(null, OwnerId);

        using var snapshot = JsonDocument.Parse(version.ToolsSnapshotJson);
        snapshot.RootElement.EnumerateArray().Should().ContainSingle(
            entry => entry.GetProperty("ToolName").GetString() == namespacedName);
    }

    [Fact]
    public void AddTool_ShouldAcceptANamespacedMcpToolName_AtTheFullFourHundredCharacterLength()
    {
        // McpTool.CreateFromDiscovery guards NamespacedName at <= 400 chars (Domain.Mcp) — this
        // confirms Agent/AgentTool never rejects a name at that same boundary.
        var namespacedName = $"mcp:{Guid.NewGuid()}:" + new string('a', 400 - $"mcp:{Guid.NewGuid()}:".Length);
        var agent = Agent.Create(
            OwnerId, "My Agent", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, OwnerId);

        var act = () => agent.AddTool(namespacedName, null, OwnerId);

        act.Should().NotThrow();
    }
}
