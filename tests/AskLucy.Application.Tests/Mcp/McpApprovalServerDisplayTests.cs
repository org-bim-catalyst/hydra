using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>
/// spec.md FR-029 — an approval request for an MCP tool call must display the target MCP server.
/// The orchestrator builds `IntendedActionDescription` from the generic, MCP-agnostic
/// `IAgentTool.Description` (unchanged code, contracts/agent-tool-contract.md) — so this is
/// satisfied entirely by `McpToolAdapter.Description` embedding the resolved server name at
/// construction time (T099a's design correction), not by any orchestrator-level MCP awareness.
/// </summary>
public sealed class McpApprovalServerDisplayTests
{
    [Fact]
    public void Description_ShouldIncludeTheSourceServersName()
    {
        var tool = McpTool.CreateFromDiscovery(
            Guid.NewGuid(), Guid.NewGuid(), "deleteRecord", "Delete Record", "Deletes a record.", "{}", "{}", null, null, "[]", null, null);
        var adapter = new McpToolAdapter(
            tool, "Acme Docs", Substitute.For<IMcpClientFactory>(), Substitute.For<IMcpRateLimiter>(), Substitute.For<IJsonSchemaValidator>(),
            new McpConnectionResiliencePolicy(Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()), Substitute.For<ILogger<McpConnectionResiliencePolicy>>()),
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()));

        adapter.Description.Should().Contain("Deletes a record.").And.Contain("Acme Docs");
    }
}
