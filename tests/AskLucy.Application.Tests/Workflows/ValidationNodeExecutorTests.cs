using System.Text.Json;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>T065d — validates against either a boolean workflow expression or a JSON Schema (contracts/workflow-node-contract.md).</summary>
public sealed class ValidationNodeExecutorTests
{
    private const string OwnerId = "user-1";

    private readonly ValidationNodeExecutor _executor = new(new WorkflowExpressionEvaluator(), new JsonSchemaValidator());

    private static WorkflowNode BuildNode(string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "node", WorkflowNodeType.Validation, "node", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_WhenTheExpressionEvaluatesToTrue()
    {
        var node = BuildNode("""{"expression":"{{workflow.score}} >= 0.8"}""");
        using var input = JsonDocument.Parse("""{"workflow.score":0.9}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheExpressionEvaluatesToFalse()
    {
        var node = BuildNode("""{"expression":"{{workflow.score}} >= 0.8"}""");
        using var input = JsonDocument.Parse("""{"workflow.score":0.2}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("evaluated to false");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheExpressionDoesNotEvaluateToABoolean()
    {
        var node = BuildNode("""{"expression":"{{workflow.score}}"}""");
        using var input = JsonDocument.Parse("""{"workflow.score":0.9}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("boolean");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_WhenTheResolvedValuesSnapshotSatisfiesTheSchema()
    {
        const string schemaJson = """{"schemaJson":{"type":"object","required":["workflow.name"],"properties":{"workflow.name":{"type":"string"}}}}""";
        var node = BuildNode(schemaJson);
        using var input = JsonDocument.Parse("""{"workflow.name":"Ada"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTheResolvedValuesSnapshotViolatesTheSchema()
    {
        const string schemaJson = """{"schemaJson":{"type":"object","required":["workflow.name"],"properties":{"workflow.name":{"type":"string"}}}}""";
        var node = BuildNode(schemaJson);
        using var input = JsonDocument.Parse("{}");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenNeitherExpressionNorSchemaJsonIsConfigured()
    {
        var node = BuildNode("{}");
        using var input = JsonDocument.Parse("{}");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("requires either");
    }
}
