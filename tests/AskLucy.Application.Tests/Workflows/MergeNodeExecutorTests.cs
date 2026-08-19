using System.Text.Json;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>T110 — all four <c>MergeNodeExecutor</c> strategies (FR-031). By the time this executor runs, the orchestrator has already flattened each applicable branch's output into <c>steps.&lt;branchNodeKey&gt;.*</c> — see the class's own doc comment for why.</summary>
public sealed class MergeNodeExecutorTests
{
    private const string OwnerId = "user-1";
    private readonly MergeNodeExecutor _executor = new();

    private static WorkflowNode BuildNode(string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "merge", WorkflowNodeType.Merge, "merge", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    [Fact]
    public async Task ExecuteAsync_AllCompleted_ShouldCombineEveryBranchsFields_PrefixedByBranchKey()
    {
        var node = BuildNode("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory"]}""");
        using var input = JsonDocument.Parse("""{"steps.rag.contextText":"rag context","steps.memory.contextText":"memory context"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("rag.contextText").GetString().Should().Be("rag context");
        result.Output!.RootElement.GetProperty("memory.contextText").GetString().Should().Be("memory context");
    }

    [Fact]
    public async Task ExecuteAsync_AllCompleted_ShouldFail_WhenNoConfiguredBranchProducedOutput()
    {
        var node = BuildNode("""{"strategy":"AllCompleted","branchNodeKeys":["rag","memory"]}""");
        using var input = JsonDocument.Parse("{}");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CollectAll_ShouldCombineEveryBranchsFields_LikeAllCompleted()
    {
        var node = BuildNode("""{"strategy":"CollectAll","branchNodeKeys":["a","b"]}""");
        using var input = JsonDocument.Parse("""{"steps.a.value":"1","steps.b.value":"2"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("a.value").GetString().Should().Be("1");
        result.Output!.RootElement.GetProperty("b.value").GetString().Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_FirstCompleted_ShouldReturnTheSingleWinningBranchsFields_UnprefixedPlusItsName()
    {
        var node = BuildNode("""{"strategy":"FirstCompleted","branchNodeKeys":["rag","memory"]}""");
        using var input = JsonDocument.Parse("""{"steps.rag.contextText":"rag context"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("branch").GetString().Should().Be("rag");
        result.Output!.RootElement.GetProperty("contextText").GetString().Should().Be("rag context");
    }

    [Fact]
    public async Task ExecuteAsync_AnyCompleted_ShouldSucceed_WhenOnlySomeBranchesProducedOutput()
    {
        var node = BuildNode("""{"strategy":"AnyCompleted","branchNodeKeys":["rag","memory"]}""");
        using var input = JsonDocument.Parse("""{"steps.memory.contextText":"memory context"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("branch").GetString().Should().Be("memory");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenBranchNodeKeysIsMissingOrEmpty()
    {
        var node = BuildNode("""{"strategy":"AllCompleted"}""");
        using var input = JsonDocument.Parse("{}");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("branchNodeKeys");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_ForAnUnknownStrategy()
    {
        var node = BuildNode("""{"strategy":"Bogus","branchNodeKeys":["a"]}""");
        using var input = JsonDocument.Parse("""{"steps.a.value":"1"}""");

        var result = await _executor.ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("Bogus");
    }
}
