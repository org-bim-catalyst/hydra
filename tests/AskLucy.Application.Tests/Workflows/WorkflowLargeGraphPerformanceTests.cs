using System.Diagnostics;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Validation;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md "Performance" section (Polish phase T201) — a large workflow graph (200 nodes, 300
/// connections) validates in under 5 seconds and publishes in under 10 seconds. Both
/// <see cref="WorkflowGraphValidator.Validate"/> and <see cref="Workflow.Publish"/> are pure,
/// in-memory operations (no I/O), so this is a regression guard against an accidentally-introduced
/// O(n²)-or-worse algorithm as the graph grows, not a load/infrastructure test.
/// </summary>
public sealed class WorkflowLargeGraphPerformanceTests
{
    private const string OwnerId = "user-1";
    private const int MiddleNodeCount = 198; // + Start + End = 200 nodes total.
    private const int SkipEdgeCount = 101; // + 199 linear-chain edges = 300 connections total.

    /// <summary>
    /// A layered DAG: Start → node1 → node2 → ... → node198 → End (199 edges), plus 101 additional
    /// forward "skip" edges (nodeI → nodeI+3) that add graph breadth without introducing a cycle —
    /// every skip edge still points from a lower chain index to a higher one.
    /// </summary>
    private static (IReadOnlyList<WorkflowNodeSpec> Nodes, IReadOnlyList<WorkflowConnectionSpec> Connections) BuildLargeGraph()
    {
        WorkflowNodeSpec Node(string key, WorkflowNodeType type, string config = "{}") =>
            new(key, type, key, null, "{}", "{}", config, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var nodes = new List<WorkflowNodeSpec> { Node("start", WorkflowNodeType.Start) };
        for (var i = 1; i <= MiddleNodeCount; i++)
        {
            nodes.Add(Node($"n{i}", WorkflowNodeType.Transform, """{"expression":"1","outputField":"result"}"""));
        }

        nodes.Add(Node("end", WorkflowNodeType.End));

        var connections = new List<WorkflowConnectionSpec> { new("start", "n1", null, null) };
        for (var i = 1; i < MiddleNodeCount; i++)
        {
            connections.Add(new WorkflowConnectionSpec($"n{i}", $"n{i + 1}", null, null));
        }

        connections.Add(new WorkflowConnectionSpec($"n{MiddleNodeCount}", "end", null, null));

        for (var i = 1; i <= SkipEdgeCount; i++)
        {
            connections.Add(new WorkflowConnectionSpec($"n{i}", $"n{i + 3}", null, null));
        }

        return (nodes, connections);
    }

    [Fact]
    public void Validate_ShouldCompleteWithinFiveSeconds_ForA200Node300ConnectionGraph()
    {
        var (nodes, connections) = BuildLargeGraph();
        nodes.Count.Should().Be(200);
        connections.Count.Should().Be(300);

        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            nodes.Select(n => new WorkflowDraftNode(n.NodeKey, n.NodeType, n.Name, n.Description, n.InputSchemaJson, n.OutputSchemaJson, n.ConfigurationJson, n.RequiredPermissionsJson, n.TimeoutSeconds, n.RetryPolicyJson, n.ApprovalPolicy, n.IdempotencyKeyExpression, n.CompensatingNodeKey, n.CanvasX, n.CanvasY)).ToList(),
            connections.Select(c => new WorkflowDraftConnection(c.SourceNodeKey, c.TargetNodeKey, c.BranchLabel, c.TypeContract)).ToList(),
            []);

        var validator = new WorkflowGraphValidator(new WorkflowExpressionEvaluator());
        var stopwatch = Stopwatch.StartNew();
        var issues = validator.Validate(draft);
        stopwatch.Stop();

        issues.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Publish_ShouldCompleteWithinTenSeconds_ForA200Node300ConnectionGraph()
    {
        var (nodes, connections) = BuildLargeGraph();
        var workflow = Workflow.Create(OwnerId, "Large Graph Workflow", null, WorkflowType.Manual, OwnerId);

        var stopwatch = Stopwatch.StartNew();
        var version = workflow.Publish(nodes, connections, [], "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}", null, OwnerId);
        stopwatch.Stop();

        version.Nodes.Should().HaveCount(200);
        version.Connections.Should().HaveCount(300);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}
