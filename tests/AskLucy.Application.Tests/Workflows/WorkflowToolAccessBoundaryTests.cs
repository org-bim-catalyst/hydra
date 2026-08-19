using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md SC-005 (Polish phase T200a) — a workflow execution using RagSearch/MemorySearch/
/// FileOperation/McpTool nodes never returns an item the executing user doesn't own, even when the
/// node's own configuration names a resource (knowledge base, document) the user has no access to.
/// The actual ownership filtering happens one layer down, inside the <see cref="IAgentTool"/>
/// implementations these node executors delegate to (<c>KnowledgeSearchTool</c>/
/// <c>MemorySearchTool</c>/<c>FileReadTool</c> — already proven at that layer by
/// <c>AgentToolAccessBoundaryTests</c>) — what this file proves is the workflow layer's own
/// contribution to that guarantee: <see cref="WorkflowNodeExecutionContext.UserId"/> (the
/// execution's initiating user, set once at start — never re-derived per node, never accepted from
/// node configuration) is passed through to <see cref="AgentToolExecutionContext.UserId"/>
/// unchanged by <see cref="WorkflowCapabilityToolInvoker"/>, and a tool's own exclusion/denial is
/// never re-widened by the node executor that wraps it. Mirrors <c>CapabilityNodeExecutorTests</c>'s
/// construction pattern exactly.
/// </summary>
public sealed class WorkflowToolAccessBoundaryTests
{
    private const string OwnerId = "user-1";
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();

    private static WorkflowNode Node(WorkflowNodeType type, string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "node", type, "node", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    private static AgentToolCatalog CatalogWith(params IAgentTool[] tools) =>
        new(tools, Substitute.For<IMcpToolRegistry>());

    private static JsonDocument EmptyInput() => JsonDocument.Parse("{}");

    [Fact]
    public async Task RagSearchNodeExecutor_ShouldPassTheExecutionsOwnerId_ToKnowledgeSearchTool_NeverAnotherUser()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("KnowledgeSearchTool");
        var seenUserId = "";
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seenUserId = ((AgentToolExecutionContext)call[0]).UserId;
                // KnowledgeSearchTool itself already excludes "kb-owned-by-someone-else" (proven by
                // AgentToolAccessBoundaryTests) — simulated here by only returning the owned chunk.
                return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = "owned-knowledge-base-content" }));
            });

        var node = Node(WorkflowNodeType.RagSearch, """{"query":"contract terms","knowledgeBaseIds":["kb-owned","kb-owned-by-someone-else"]}""");
        var executor = new RagSearchNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        seenUserId.Should().Be(OwnerId);
        result.Output!.RootElement.GetProperty("contextText").GetString().Should().Be("owned-knowledge-base-content");
    }

    [Fact]
    public async Task MemorySearchNodeExecutor_ShouldPassTheExecutionsOwnerId_ToMemorySearchTool_NeverAnotherUser()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("MemorySearchTool");
        var seenUserId = "";
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seenUserId = ((AgentToolExecutionContext)call[0]).UserId;
                // MemorySearchTool itself already scopes retrieval to context.UserId (proven by
                // AgentToolAccessBoundaryTests) — simulated here by returning only this user's memory.
                return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { memories = new[] { "owner's own memory" } }));
            });

        var node = Node(WorkflowNodeType.MemorySearch, """{"query":"project preferences"}""");
        var executor = new MemorySearchNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        seenUserId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task FileOperationNodeExecutor_ShouldNotSwallowOrReinterpret_TheUnderlyingToolsOwnershipDenial()
    {
        // DocumentOwnershipGuard.EnsureOwnedBy throws KeyNotFoundException (404-shaped, never 403)
        // for a document the caller doesn't own — this test proves FileOperationNodeExecutor lets
        // that denial propagate unchanged rather than translating it into a successful result.
        var readTool = Substitute.For<IAgentTool>();
        readTool.Name.Returns("FileReadTool");
        readTool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Throws(new KeyNotFoundException("Document not found."));

        var node = Node(WorkflowNodeType.FileOperation, """{"operation":"Read","documentId":"11111111-1111-1111-1111-111111111111"}""");
        var executor = new FileOperationNodeExecutor(CatalogWith(readTool), _expressionEvaluator);

        var act = () => executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task McpToolNodeExecutor_ShouldPassTheExecutionsOwnerId_ToTheMcpTool_NeverAnotherUser()
    {
        // McpToolAdapter has no dependency on any Knowledge Base/File/Memory abstraction
        // (structurally proven by McpAuthorizationBypassSecurityTests) — an MCP tool's own item
        // ownership is enforced by the external MCP server, outside this codebase's control. What
        // the workflow layer itself is responsible for — and what this test proves — is that it
        // never substitutes a different user's id into the call it makes.
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("mcp:server-1:SearchRecords");
        var seenUserId = "";
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seenUserId = ((AgentToolExecutionContext)call[0]).UserId;
                return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { records = Array.Empty<object>() }));
            });

        var node = Node(WorkflowNodeType.McpTool, """{"toolName":"mcp:server-1:SearchRecords","input":{}}""");
        var executor = new McpToolNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        seenUserId.Should().Be(OwnerId);
    }
}
