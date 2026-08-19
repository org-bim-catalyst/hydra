using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Validation;

/// <summary>One publish-blocking (or validate-time) violation (FR-016) — <see cref="NodeKey"/> is null for a workflow-level issue.</summary>
public sealed record WorkflowValidationIssue(string? NodeKey, string Message);

/// <summary>
/// Runs every FR-016 publish-blocking rule against a parsed <see cref="WorkflowDraftDefinition"/> —
/// used by both <c>ValidateWorkflowCommand</c> (draft-time, non-blocking) and
/// <c>PublishWorkflowVersionCommand</c> (publish-time, blocking on any issue, SC-009). Never
/// mutates the draft; purely a read-only structural/expression check.
/// </summary>
public sealed partial class WorkflowGraphValidator(IWorkflowExpressionEvaluator expressionEvaluator)
{
    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("\"maxIterations\"\\s*:")]
    private static partial Regex MaxIterationsPattern();

    public IReadOnlyList<WorkflowValidationIssue> Validate(WorkflowDraftDefinition draft)
    {
        var issues = new List<WorkflowValidationIssue>();

        if (draft.Nodes.Count == 0)
        {
            issues.Add(new WorkflowValidationIssue(null, "The workflow has no nodes."));
            return issues;
        }

        var nodeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in draft.Nodes)
        {
            if (!nodeKeys.Add(node.NodeKey))
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Duplicate node key '{node.NodeKey}'."));
            }
        }

        ValidateStartAndEnd(draft, issues);
        ValidateConnections(draft, nodeKeys, issues);
        ValidateConnectionTypeCompatibility(draft, issues);
        ValidateNoDisconnectedNodes(draft, issues);
        ValidateNoUnsupportedCycles(draft, issues);
        ValidateBoundedLoops(draft, issues);
        ValidateCompensatingNodes(draft, nodeKeys, issues);
        ValidateNodeConfigurationIsJson(draft, issues);

        var knownReferences = BuildKnownReferences(draft);
        ValidateVariableReferences(draft, knownReferences, issues);
        ValidateExpressions(draft, knownReferences, issues);

        if (string.IsNullOrWhiteSpace(draft.ErrorPolicyJson) || draft.ErrorPolicyJson == "{}")
        {
            issues.Add(new WorkflowValidationIssue(null, "The workflow is missing an error policy."));
        }

        return issues;
    }

    private static void ValidateStartAndEnd(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        var startCount = draft.Nodes.Count(n => n.NodeType == WorkflowNodeType.Start);
        if (startCount == 0)
        {
            issues.Add(new WorkflowValidationIssue(null, "The workflow is missing a Start node."));
        }
        else if (startCount > 1)
        {
            issues.Add(new WorkflowValidationIssue(null, "A workflow may only have one Start node."));
        }

        if (draft.Nodes.All(n => n.NodeType != WorkflowNodeType.End))
        {
            issues.Add(new WorkflowValidationIssue(null, "The workflow is missing an End node."));
        }
    }

    private static void ValidateConnections(WorkflowDraftDefinition draft, HashSet<string> nodeKeys, List<WorkflowValidationIssue> issues)
    {
        foreach (var connection in draft.Connections)
        {
            if (!nodeKeys.Contains(connection.SourceNodeKey))
            {
                issues.Add(new WorkflowValidationIssue(connection.SourceNodeKey, $"A connection references an unknown source node '{connection.SourceNodeKey}'."));
            }

            if (!nodeKeys.Contains(connection.TargetNodeKey))
            {
                issues.Add(new WorkflowValidationIssue(connection.TargetNodeKey, $"A connection references an unknown target node '{connection.TargetNodeKey}'."));
            }
        }
    }

    private static readonly HashSet<string> NumericSchemaTypes = new(StringComparer.Ordinal) { "number", "integer" };

    /// <summary>
    /// FR-008 — rejects a connection only when BOTH its source node's declared output schema and
    /// its target node's declared input schema independently declare an explicit, differing
    /// scalar JSON Schema <c>"type"</c> (e.g. source <c>"number"</c> into target <c>"string"</c>).
    /// An undeclared type, or a <c>"object"</c>/<c>"array"</c> schema (every node's real output is
    /// a named-field object, not a single connectable scalar), never blocks a connection — mirrors
    /// `WorkflowCanvas.tsx`'s client-side `isValidConnection` check exactly, so an author never
    /// sees a connection accepted by the Designer and then rejected at publish, or vice versa.
    /// </summary>
    private static void ValidateConnectionTypeCompatibility(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        // A duplicate NodeKey is already reported by the Validate() caller's own check above —
        // keep the first occurrence here rather than throwing, so this method never duplicates
        // that report or crashes on input the duplicate-key check already flagged as invalid.
        var nodesByKey = new Dictionary<string, WorkflowDraftNode>(StringComparer.Ordinal);
        foreach (var node in draft.Nodes)
        {
            nodesByKey.TryAdd(node.NodeKey, node);
        }

        foreach (var connection in draft.Connections)
        {
            if (!nodesByKey.TryGetValue(connection.SourceNodeKey, out var source) || !nodesByKey.TryGetValue(connection.TargetNodeKey, out var target))
            {
                continue; // Already reported by ValidateConnections — never double-report.
            }

            var sourceType = TryGetScalarSchemaType(source.OutputSchemaJson);
            var targetType = TryGetScalarSchemaType(target.InputSchemaJson);
            if (sourceType is null || targetType is null || sourceType == targetType)
            {
                continue;
            }

            if (NumericSchemaTypes.Contains(sourceType) && NumericSchemaTypes.Contains(targetType))
            {
                continue;
            }

            issues.Add(new WorkflowValidationIssue(
                target.NodeKey,
                $"Connection from '{source.NodeKey}' (output type '{sourceType}') to '{target.NodeKey}' (input type '{targetType}') is not type-compatible."));
        }
    }

    /// <summary>Reads a schema document's own top-level <c>"type"</c> keyword, if it declares a scalar one — <see langword="null"/> for missing/malformed/<c>object</c>/<c>array</c> schemas.</summary>
    private static string? TryGetScalarSchemaType(string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            if (!document.RootElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var type = typeElement.GetString();
            return type is null or "object" or "array" ? null : type;
        }
        catch (JsonException)
        {
            return null; // Already reported by ValidateNodeConfigurationIsJson — never double-report or throw unhandled.
        }
    }

    /// <summary>FR-016 — every non-Start node needs an incoming connection, every non-End node needs an outgoing one (skipped entirely for a single-node workflow, which is trivially connected).</summary>
    private static void ValidateNoDisconnectedNodes(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        if (draft.Nodes.Count <= 1)
        {
            return;
        }

        var nodesWithIncoming = draft.Connections.Select(c => c.TargetNodeKey).ToHashSet(StringComparer.Ordinal);
        var nodesWithOutgoing = draft.Connections.Select(c => c.SourceNodeKey).ToHashSet(StringComparer.Ordinal);

        foreach (var node in draft.Nodes)
        {
            if (node.NodeType != WorkflowNodeType.Start && !nodesWithIncoming.Contains(node.NodeKey))
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Node '{node.NodeKey}' is disconnected — it has no incoming connection."));
            }

            if (node.NodeType != WorkflowNodeType.End && !nodesWithOutgoing.Contains(node.NodeKey))
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Node '{node.NodeKey}' is disconnected — it has no outgoing connection."));
            }
        }
    }

    /// <summary>research.md Decision 20 — a connection labeled <see cref="WorkflowConnection.LoopBackBranchLabel"/> is an intentional, bounded construct and is excluded from cycle detection; every other cycle is rejected.</summary>
    private static void ValidateNoUnsupportedCycles(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        var adjacency = draft.Connections
            .Where(c => c.BranchLabel != WorkflowConnection.LoopBackBranchLabel)
            .GroupBy(c => c.SourceNodeKey)
            .ToDictionary(g => g.Key, g => g.Select(c => c.TargetNodeKey).ToList());

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in draft.Nodes)
        {
            if (!visited.Contains(node.NodeKey) && HasCycle(node.NodeKey, adjacency, visiting, visited))
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, "The workflow contains an unsupported circular dependency."));
                return;
            }
        }
    }

    private static bool HasCycle(string nodeKey, IReadOnlyDictionary<string, List<string>> adjacency, HashSet<string> visiting, HashSet<string> visited)
    {
        visiting.Add(nodeKey);

        if (adjacency.TryGetValue(nodeKey, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (visiting.Contains(neighbor))
                {
                    return true;
                }

                if (!visited.Contains(neighbor) && HasCycle(neighbor, adjacency, visiting, visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(nodeKey);
        visited.Add(nodeKey);
        return false;
    }

    /// <summary>FR-032 — a loop-back edge's target (the loop body's first node) must declare a maximum iteration count in its configuration; the system MUST NEVER allow an unbounded loop.</summary>
    private static void ValidateBoundedLoops(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        foreach (var loopBack in draft.Connections.Where(c => c.BranchLabel == WorkflowConnection.LoopBackBranchLabel))
        {
            var loopBodyFirstNode = draft.Nodes.FirstOrDefault(n => n.NodeKey == loopBack.TargetNodeKey);
            if (loopBodyFirstNode is not null && !MaxIterationsPattern().IsMatch(loopBodyFirstNode.ConfigurationJson))
            {
                issues.Add(new WorkflowValidationIssue(loopBodyFirstNode.NodeKey,
                    $"Loop body '{loopBodyFirstNode.NodeKey}' must declare a maximum iteration count in its configuration — unbounded loops are never allowed (FR-032)."));
            }
        }
    }

    private static void ValidateCompensatingNodes(WorkflowDraftDefinition draft, HashSet<string> nodeKeys, List<WorkflowValidationIssue> issues)
    {
        foreach (var node in draft.Nodes.Where(n => n.CompensatingNodeKey is not null))
        {
            if (node.CompensatingNodeKey == node.NodeKey)
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Node '{node.NodeKey}' cannot compensate itself."));
            }
            else if (!nodeKeys.Contains(node.CompensatingNodeKey!))
            {
                issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Node '{node.NodeKey}' declares a compensating node '{node.CompensatingNodeKey}' that does not exist."));
            }
        }
    }

    private static void ValidateNodeConfigurationIsJson(WorkflowDraftDefinition draft, List<WorkflowValidationIssue> issues)
    {
        foreach (var node in draft.Nodes)
        {
            ValidateIsJson(node.ConfigurationJson, node.NodeKey, "configuration", issues);
            ValidateIsJson(node.InputSchemaJson, node.NodeKey, "input schema", issues);
            ValidateIsJson(node.OutputSchemaJson, node.NodeKey, "output schema", issues);
            ValidateIsJson(node.RequiredPermissionsJson, node.NodeKey, "required permissions", issues);
        }
    }

    private static void ValidateIsJson(string json, string nodeKey, string fieldLabel, List<WorkflowValidationIssue> issues)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            issues.Add(new WorkflowValidationIssue(nodeKey, $"Node '{nodeKey}' has an invalid {fieldLabel} — it is not valid JSON."));
        }
    }

    /// <summary>Valid reference prefixes: <c>workflow.{variableName}</c> for a declared variable, or <c>steps.{nodeKey}.*</c> for any field of a known node's output (exact output field names are not statically enforced at this layer).</summary>
    private static HashSet<string> BuildKnownReferences(WorkflowDraftDefinition draft)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in draft.Variables)
        {
            references.Add($"workflow.{variable.Name}");
        }

        foreach (var node in draft.Nodes)
        {
            references.Add($"steps.{node.NodeKey}");
        }

        return references;
    }

    private static bool IsKnownReference(string path, HashSet<string> knownReferences)
    {
        if (knownReferences.Contains(path))
        {
            return true;
        }

        var firstDot = path.IndexOf('.');
        if (firstDot < 0)
        {
            return false;
        }

        var prefix = path[..firstDot];
        if (prefix is "system" or "environment")
        {
            return true; // FR-026 — SystemContext/EnvironmentConfiguration references are resolved by the runtime, not declared as workflow variables.
        }

        if (prefix != "steps")
        {
            return knownReferences.Contains(path);
        }

        var secondDot = path.IndexOf('.', firstDot + 1);
        var nodeKeyPortion = secondDot < 0 ? path[(firstDot + 1)..] : path[(firstDot + 1)..secondDot];
        return knownReferences.Contains($"steps.{nodeKeyPortion}");
    }

    /// <summary>FR-016 "invalid variable references" — scans every node's raw configuration text for <c>{{...}}</c> placeholders (a pragmatic text scan rather than a full JSON-schema walk; well-formed configuration only ever embeds a reference inside a string value, so this still finds every real occurrence).</summary>
    private static void ValidateVariableReferences(WorkflowDraftDefinition draft, HashSet<string> knownReferences, List<WorkflowValidationIssue> issues)
    {
        foreach (var node in draft.Nodes)
        {
            foreach (Match match in ReferencePattern().Matches(node.ConfigurationJson))
            {
                var path = match.Groups[1].Value.Trim();
                if (!IsKnownReference(path, knownReferences))
                {
                    issues.Add(new WorkflowValidationIssue(node.NodeKey, $"Node '{node.NodeKey}' references an unknown variable or step output '{{{{{path}}}}}'."));
                }
            }
        }
    }

    /// <summary>FR-028 — Condition/Transform/Validation node configuration expressions (and every node's idempotency-key expression, FR-043) must parse and type-check before publish.</summary>
    private void ValidateExpressions(WorkflowDraftDefinition draft, HashSet<string> knownReferences, List<WorkflowValidationIssue> issues)
    {
        var knownTypes = BuildKnownVariableTypes(draft);

        foreach (var node in draft.Nodes.Where(n => n.NodeType is WorkflowNodeType.Condition or WorkflowNodeType.Transform or WorkflowNodeType.Validation))
        {
            List<string> expressions;
            try
            {
                expressions = ExtractExpressionStrings(node.ConfigurationJson).ToList();
            }
            catch (JsonException)
            {
                continue; // Already reported by ValidateNodeConfigurationIsJson — never double-report or throw unhandled.
            }

            foreach (var expression in expressions)
            {
                ValidateExpressionString(node.NodeKey, expression, knownTypes, issues);
            }
        }

        foreach (var node in draft.Nodes.Where(n => n.IdempotencyKeyExpression is not null))
        {
            ValidateExpressionString(node.NodeKey, node.IdempotencyKeyExpression!, knownTypes, issues);
        }
    }

    private void ValidateExpressionString(string nodeKey, string expression, IReadOnlyDictionary<string, WorkflowVariableType> knownTypes, List<WorkflowValidationIssue> issues)
    {
        WorkflowExpressionNode ast;
        try
        {
            ast = expressionEvaluator.Parse(expression);
        }
        catch (WorkflowExpressionParseException ex)
        {
            issues.Add(new WorkflowValidationIssue(nodeKey, $"Node '{nodeKey}' has an invalid expression: {ex.Message}"));
            return;
        }

        foreach (var typeError in expressionEvaluator.ValidateTypes(ast, knownTypes))
        {
            issues.Add(new WorkflowValidationIssue(nodeKey, $"Node '{nodeKey}': {typeError}"));
        }
    }

    private static Dictionary<string, WorkflowVariableType> BuildKnownVariableTypes(WorkflowDraftDefinition draft)
    {
        var types = new Dictionary<string, WorkflowVariableType>(StringComparer.Ordinal);
        foreach (var variable in draft.Variables)
        {
            types[$"workflow.{variable.Name}"] = variable.ValueType;
        }

        return types;
    }

    /// <summary>A node's configuration conventionally carries its expression(s) under a top-level <c>"expression"</c> string property, or an array of them under <c>"expressions"</c> — a Transform node may map several fields, each via its own expression.</summary>
    private static IEnumerable<string> ExtractExpressionStrings(string configurationJson)
    {
        using var document = JsonDocument.Parse(configurationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (document.RootElement.TryGetProperty("expression", out var single) && single.ValueKind == JsonValueKind.String)
        {
            yield return single.GetString()!;
        }

        if (document.RootElement.TryGetProperty("expressions", out var many) && many.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in many.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    yield return item.GetString()!;
                }
            }
        }
    }
}
