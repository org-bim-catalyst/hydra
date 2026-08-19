using AskLucy.Application.Agents.Tools;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>research.md Decision 1 — <see cref="AgentToolCatalog"/> merges native DI-registered tools with <see cref="IMcpToolRegistry"/>'s dynamic, currently-active MCP tools.</summary>
public sealed class AgentToolCatalogCompositionTests
{
    private static IAgentTool FakeTool(string name)
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns(name);
        return tool;
    }

    [Fact]
    public void Find_ShouldResolveNativeTool_ByPlainName()
    {
        var native = FakeTool("KnowledgeSearchTool");
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        var catalog = new AgentToolCatalog([native], registry);

        catalog.Find("KnowledgeSearchTool").Should().BeSameAs(native);
    }

    [Fact]
    public void Find_ShouldResolveMcpTool_ByNamespacedName()
    {
        var mcpTool = FakeTool("mcp:11111111-1111-1111-1111-111111111111:search");
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[mcpTool]);
        var catalog = new AgentToolCatalog([], registry);

        catalog.Find("mcp:11111111-1111-1111-1111-111111111111:search").Should().BeSameAs(mcpTool);
    }

    [Fact]
    public void Find_ShouldReturnNull_ForUnknownName()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        var catalog = new AgentToolCatalog([], registry);

        catalog.Find("DoesNotExist").Should().BeNull();
    }

    [Fact]
    public void All_ShouldIncludeBothNativeAndMcpTools()
    {
        var native = FakeTool("FileReadTool");
        var mcpTool = FakeTool("mcp:22222222-2222-2222-2222-222222222222:search");
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[mcpTool]);
        var catalog = new AgentToolCatalog([native], registry);

        catalog.All.Should().BeEquivalentTo([native, mcpTool]);
    }

    [Fact]
    public void All_ShouldReflectRegistryChange_OnNextCall()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        var catalog = new AgentToolCatalog([], registry);

        catalog.All.Should().BeEmpty();

        var newlyActivated = FakeTool("mcp:33333333-3333-3333-3333-333333333333:search");
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[newlyActivated]);

        catalog.All.Should().ContainSingle().Which.Should().BeSameAs(newlyActivated);
    }
}
