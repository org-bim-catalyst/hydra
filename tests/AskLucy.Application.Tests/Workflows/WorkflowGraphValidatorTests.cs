using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Validation;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class WorkflowGraphValidatorTests
{
    private readonly WorkflowGraphValidator _validator = new(new WorkflowExpressionEvaluator());

    private static WorkflowDraftNode Node(
        string key, WorkflowNodeType type, string configurationJson = "{}", string? compensatingNodeKey = null, string? idempotencyKeyExpression = null) =>
        new(key, type, key, null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, idempotencyKeyExpression, compensatingNodeKey, 0, 0);

    private static WorkflowDraftDefinition ValidLinearWorkflow() => new(
        "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
        [Node("start", WorkflowNodeType.Start), Node("transform", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
        [new WorkflowDraftConnection("start", "transform", null, null), new WorkflowDraftConnection("transform", "end", null, null)],
        []);

    [Fact]
    public void Validate_ShouldPass_ForAValidLinearWorkflow()
    {
        var issues = _validator.Validate(ValidLinearWorkflow());

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenNoNodesExist()
    {
        var draft = ValidLinearWorkflow() with { Nodes = [] };

        var issues = _validator.Validate(draft);

        issues.Should().ContainSingle();
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenStartNodeIsMissing()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = draft.Nodes.Where(n => n.NodeType != WorkflowNodeType.Start).ToList() };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("Start node"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenEndNodeIsMissing()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = draft.Nodes.Where(n => n.NodeType != WorkflowNodeType.End).ToList() };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("End node"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForADisconnectedNode()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = [.. draft.Nodes, Node("orphan", WorkflowNodeType.Transform)] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.NodeKey == "orphan");
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForAConnectionToAnUnknownNode()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Connections = [.. draft.Connections, new WorkflowDraftConnection("end", "does-not-exist", null, null)] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("unknown target node"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForAnUnsupportedCycle()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [Node("start", WorkflowNodeType.Start), Node("a", WorkflowNodeType.Transform), Node("b", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [
                new WorkflowDraftConnection("start", "a", null, null),
                new WorkflowDraftConnection("a", "b", null, null),
                new WorkflowDraftConnection("b", "a", null, null), // unsupported cycle — not loop-back labeled
                new WorkflowDraftConnection("b", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("circular dependency"));
    }

    [Fact]
    public void Validate_ShouldAllowALoopBackEdge_WhenTheLoopBodyDeclaresMaxIterations()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [Node("start", WorkflowNodeType.Start), Node("loopBody", WorkflowNodeType.Transform, "{\"maxIterations\":10}"), Node("end", WorkflowNodeType.End)],
            [
                new WorkflowDraftConnection("start", "loopBody", null, null),
                new WorkflowDraftConnection("loopBody", "loopBody", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowDraftConnection("loopBody", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenALoopBackEdgesTargetHasNoMaxIterations()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [Node("start", WorkflowNodeType.Start), Node("loopBody", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [
                new WorkflowDraftConnection("start", "loopBody", null, null),
                new WorkflowDraftConnection("loopBody", "loopBody", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowDraftConnection("loopBody", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("maximum iteration count"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenANodeCompensatesItself()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, compensatingNodeKey: "transform")] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("cannot compensate itself"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForInvalidNodeConfigurationJson()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, "{not valid json")] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("not valid JSON"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForAnUnknownVariableReference()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with
        {
            Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, "{\"value\":\"{{workflow.doesNotExist}}\"}")],
        };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("unknown variable or step output"));
    }

    [Fact]
    public void Validate_ShouldAllowAKnownVariableReference()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with
        {
            Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, "{\"value\":\"{{workflow.threshold}}\"}")],
            Variables = [new WorkflowDraftVariable("threshold", WorkflowVariableKind.WorkflowVariable, WorkflowVariableType.Number, null, false)],
        };

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldAllowAKnownStepOutputReference()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with
        {
            Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, "{\"value\":\"{{steps.start.result}}\"}")],
        };

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForAnUnparsableConditionExpression()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with
        {
            Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Condition, "{\"expression\":\"1; 2\"}")],
        };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("invalid expression"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForATypeMismatchedConditionExpression()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with
        {
            Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Condition, "{\"expression\":\"{{workflow.name}} > 5\"}")],
            Variables = [new WorkflowDraftVariable("name", WorkflowVariableKind.WorkflowVariable, WorkflowVariableType.String, null, false)],
        };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.NodeKey == "transform");
    }

    [Fact]
    public void Validate_ShouldValidateIdempotencyKeyExpressions()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = [.. draft.Nodes.Where(n => n.NodeKey != "transform"), Node("transform", WorkflowNodeType.Transform, idempotencyKeyExpression: "1; 2")] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("invalid expression"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenErrorPolicyIsMissing()
    {
        var draft = ValidLinearWorkflow() with { ErrorPolicyJson = "{}" };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("error policy"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForDuplicateNodeKeys()
    {
        var draft = ValidLinearWorkflow();
        draft = draft with { Nodes = [.. draft.Nodes, Node("start", WorkflowNodeType.Transform)] };

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("Duplicate node key"));
    }

    // FR-008 — connection type-compatibility (T087).

    private static WorkflowDraftDefinition LinearWorkflowWithSchemas(string startOutputSchemaJson, string transformInputSchemaJson)
    {
        var draft = ValidLinearWorkflow();
        return draft with
        {
            Nodes =
            [
                draft.Nodes.Single(n => n.NodeKey == "start") with { OutputSchemaJson = startOutputSchemaJson },
                draft.Nodes.Single(n => n.NodeKey == "transform") with { InputSchemaJson = transformInputSchemaJson },
                draft.Nodes.Single(n => n.NodeKey == "end"),
            ],
        };
    }

    [Fact]
    public void Validate_ShouldReportIssue_WhenAConnectionsEndpointsDeclareDifferingScalarTypes()
    {
        var draft = LinearWorkflowWithSchemas("""{"type":"number"}""", """{"type":"string"}""");

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.NodeKey == "transform" && i.Message.Contains("not type-compatible"));
    }

    [Fact]
    public void Validate_ShouldPass_WhenAConnectionsEndpointsDeclareTheSameScalarType()
    {
        var draft = LinearWorkflowWithSchemas("""{"type":"string"}""", """{"type":"string"}""");

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldPass_WhenSourceAndTargetNumericTypesDiffer_NumberAndInteger()
    {
        var draft = LinearWorkflowWithSchemas("""{"type":"integer"}""", """{"type":"number"}""");

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"type":"number"}""", "{}")]
    [InlineData("{}", """{"type":"string"}""")]
    [InlineData("""{"type":"object"}""", """{"type":"string"}""")]
    public void Validate_ShouldPass_WhenEitherEndpointDeclaresNoScalarType(string startOutputSchemaJson, string transformInputSchemaJson)
    {
        var draft = LinearWorkflowWithSchemas(startOutputSchemaJson, transformInputSchemaJson);

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }

    // T112 — additional unsupported-cycle-detection cases (FR-016), beyond the two-node case
    // Validate_ShouldReportIssue_ForAnUnsupportedCycle above already covers.

    [Fact]
    public void Validate_ShouldReportIssue_ForASelfLoopingConnection_NotLabeledLoopBack()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [Node("start", WorkflowNodeType.Start), Node("a", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [
                new WorkflowDraftConnection("start", "a", null, null),
                new WorkflowDraftConnection("a", "a", null, null), // self-loop, not loop-back labeled
                new WorkflowDraftConnection("a", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("circular dependency"));
    }

    [Fact]
    public void Validate_ShouldReportIssue_ForAThreeNodeUnsupportedCycle()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [Node("start", WorkflowNodeType.Start), Node("a", WorkflowNodeType.Transform), Node("b", WorkflowNodeType.Transform), Node("c", WorkflowNodeType.Transform), Node("end", WorkflowNodeType.End)],
            [
                new WorkflowDraftConnection("start", "a", null, null),
                new WorkflowDraftConnection("a", "b", null, null),
                new WorkflowDraftConnection("b", "c", null, null),
                new WorkflowDraftConnection("c", "a", null, null), // 3-node cycle, not loop-back labeled
                new WorkflowDraftConnection("c", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().Contain(i => i.Message.Contains("circular dependency"));
    }

    [Fact]
    public void Validate_ShouldAllowTwoIndependentLoopBackEdges_InTheSameWorkflow()
    {
        var draft = new WorkflowDraftDefinition(
            "{}", "{}", "{\"strategy\":\"Stop\"}", "{}", "{}",
            [
                Node("start", WorkflowNodeType.Start),
                Node("loopA", WorkflowNodeType.Transform, "{\"maxIterations\":5}"),
                Node("loopB", WorkflowNodeType.Transform, "{\"maxIterations\":5}"),
                Node("end", WorkflowNodeType.End),
            ],
            [
                new WorkflowDraftConnection("start", "loopA", null, null),
                new WorkflowDraftConnection("loopA", "loopA", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowDraftConnection("loopA", "loopB", null, null),
                new WorkflowDraftConnection("loopB", "loopB", WorkflowConnection.LoopBackBranchLabel, null),
                new WorkflowDraftConnection("loopB", "end", null, null),
            ],
            []);

        var issues = _validator.Validate(draft);

        issues.Should().BeEmpty();
    }
}
