using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// T065a — delegation correctness for the six thin-adapter executors that wrap an existing
/// <see cref="IAgentTool"/> via <see cref="AgentToolCatalog"/> (research.md Decision 1,
/// contracts/workflow-node-contract.md). Each test proves the executor resolves its
/// configuration (including <c>{{...}}</c> expression fields) into the exact input the
/// underlying tool expects, and translates the tool's own success/failure back into a
/// <see cref="WorkflowNodeExecutionResult"/> unchanged.
/// </summary>
public sealed class CapabilityNodeExecutorTests
{
    private const string OwnerId = "user-1";
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();

    /// <summary>Real <see cref="WorkflowNode"/> entities can only be materialized via <see cref="Workflow.Publish"/> (internal factory) — mirrors <c>WorkflowExecutionOrchestratorTests</c>'s own pattern.</summary>
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

    private static JsonDocument EmptyInput() => JsonDocument.Parse("{\"workflow.q\":\"hello\"}");

    [Fact]
    public async Task RagSearchNodeExecutor_ShouldPassTheResolvedQueryAndKnowledgeBaseIds_ToKnowledgeSearchTool()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("KnowledgeSearchTool");
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = (JsonDocument)call[1];
                input.RootElement.GetProperty("query").GetString().Should().Be("hello");
                input.RootElement.GetProperty("knowledgeBaseIds").GetArrayLength().Should().Be(1);
                return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = "grounded" }));
            });

        var node = Node(WorkflowNodeType.RagSearch, """{"query":"{{workflow.q}}","knowledgeBaseIds":["kb-1"]}""");
        var executor = new RagSearchNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("contextText").GetString().Should().Be("grounded");
    }

    [Fact]
    public async Task RagSearchNodeExecutor_ShouldFail_WhenKnowledgeSearchToolIsNotRegistered()
    {
        var node = Node(WorkflowNodeType.RagSearch, """{"query":"static text"}""");
        var executor = new RagSearchNodeExecutor(CatalogWith(), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("KnowledgeSearchTool");
    }

    [Fact]
    public async Task MemorySearchNodeExecutor_ShouldDelegateToMemorySearchTool_AndPropagateFailure()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("MemorySearchTool");
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Failure("Memory search is temporarily unavailable."));

        var node = Node(WorkflowNodeType.MemorySearch, """{"query":"static text"}""");
        var executor = new MemorySearchNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("Memory search is temporarily unavailable.");
    }

    [Fact]
    public async Task DocumentProcessingNodeExecutor_ShouldDelegateToDocumentSearchTool()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("DocumentSearchTool");
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { documents = Array.Empty<object>() })));

        var node = Node(WorkflowNodeType.DocumentProcessing, """{"query":"invoices"}""");
        var executor = new DocumentProcessingNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Read", "FileReadTool")]
    [InlineData("Metadata", "FileMetadataTool")]
    public async Task FileOperationNodeExecutor_ShouldRouteToTheConfiguredOperationsTool(string operation, string expectedToolName)
    {
        var readTool = Substitute.For<IAgentTool>();
        readTool.Name.Returns("FileReadTool");
        readTool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { content = "read" })));

        var metadataTool = Substitute.For<IAgentTool>();
        metadataTool.Name.Returns("FileMetadataTool");
        metadataTool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { fileName = "a.txt" })));

        var node = Node(WorkflowNodeType.FileOperation, $$"""{"operation":"{{operation}}","documentId":"11111111-1111-1111-1111-111111111111"}""");
        var executor = new FileOperationNodeExecutor(CatalogWith(readTool, metadataTool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var invokedTool = expectedToolName == "FileReadTool" ? readTool : metadataTool;
        await invokedTool.Received(1).ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task McpToolNodeExecutor_ShouldFail_WhenToolNameIsNotMcpNamespaced()
    {
        var node = Node(WorkflowNodeType.McpTool, """{"toolName":"KnowledgeSearchTool","input":{}}""");
        var executor = new McpToolNodeExecutor(CatalogWith(), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Native Tool");
    }

    [Fact]
    public async Task McpToolNodeExecutor_ShouldResolveExpressionFieldsInInput_AndInvokeTheNamedMcpTool()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("mcp:server-1:SendMessage");
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = (JsonDocument)call[1];
                input.RootElement.GetProperty("message").GetString().Should().Be("hello");
                return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { sent = true }));
            });

        var node = Node(WorkflowNodeType.McpTool, """{"toolName":"mcp:server-1:SendMessage","input":{"message":"{{workflow.q}}"}}""");
        var executor = new McpToolNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task NativeToolNodeExecutor_ShouldFail_WhenToolNameIsMcpNamespaced()
    {
        var node = Node(WorkflowNodeType.NativeTool, """{"toolName":"mcp:server-1:SendMessage","input":{}}""");
        var executor = new NativeToolNodeExecutor(CatalogWith(), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("MCP Tool");
    }

    [Fact]
    public async Task NativeToolNodeExecutor_ShouldDelegateToTheNamedNativeTool()
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("ConversationTool");
        tool.ExecuteAsync(Arg.Any<AgentToolExecutionContext>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { messages = Array.Empty<object>() })));

        var node = Node(WorkflowNodeType.NativeTool, """{"toolName":"ConversationTool","input":{"maxMessages":5}}""");
        var executor = new NativeToolNodeExecutor(CatalogWith(tool), _expressionEvaluator);

        var result = await executor.ExecuteAsync(Context(node), EmptyInput(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }
}
