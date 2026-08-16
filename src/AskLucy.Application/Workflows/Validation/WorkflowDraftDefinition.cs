using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Validation;

/// <summary>Parsed shape of a node inside <see cref="Workflow.DraftDefinitionJson"/> — the Designer's canvas document (research.md Decision 19). Converts 1:1 to a <see cref="WorkflowNodeSpec"/>.</summary>
public sealed record WorkflowDraftNode(
    string NodeKey,
    WorkflowNodeType NodeType,
    string Name,
    string? Description,
    string InputSchemaJson,
    string OutputSchemaJson,
    string ConfigurationJson,
    string RequiredPermissionsJson,
    int? TimeoutSeconds,
    string? RetryPolicyJson,
    WorkflowNodeApprovalPolicy ApprovalPolicy,
    string? IdempotencyKeyExpression,
    string? CompensatingNodeKey,
    double CanvasX,
    double CanvasY)
{
    public WorkflowNodeSpec ToSpec() => new(
        NodeKey, NodeType, Name, Description, InputSchemaJson, OutputSchemaJson, ConfigurationJson,
        RequiredPermissionsJson, TimeoutSeconds, RetryPolicyJson, ApprovalPolicy, IdempotencyKeyExpression,
        CompensatingNodeKey, CanvasX, CanvasY);
}

public sealed record WorkflowDraftConnection(string SourceNodeKey, string TargetNodeKey, string? BranchLabel, string? TypeContract)
{
    public WorkflowConnectionSpec ToSpec() => new(SourceNodeKey, TargetNodeKey, BranchLabel, TypeContract);
}

public sealed record WorkflowDraftVariable(string Name, WorkflowVariableKind Kind, WorkflowVariableType ValueType, string? DefaultValueJson, bool IsRequired)
{
    public WorkflowVariableSpec ToSpec() => new(Name, Kind, ValueType, DefaultValueJson, IsRequired);
}

/// <summary>
/// The parsed shape of <see cref="Workflow.DraftDefinitionJson"/> — shared by <c>ValidateWorkflowCommand</c>
/// and <c>PublishWorkflowVersionCommand</c> so both run against the exact same parsing logic
/// (research.md Decision 19). A malformed draft (invalid JSON) is reported as a single validation
/// issue by <see cref="TryParse"/>'s caller, never an unhandled exception surfaced to the user
/// (constitution §2.VIII).
/// </summary>
public sealed record WorkflowDraftDefinition(
    string InputsSchemaJson,
    string OutputsSchemaJson,
    string ErrorPolicyJson,
    string ExecutionPolicyJson,
    string SecurityPolicyJson,
    IReadOnlyList<WorkflowDraftNode> Nodes,
    IReadOnlyList<WorkflowDraftConnection> Connections,
    IReadOnlyList<WorkflowDraftVariable> Variables)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Envelope(
        string? InputsSchemaJson, string? OutputsSchemaJson, string? ErrorPolicyJson, string? ExecutionPolicyJson, string? SecurityPolicyJson,
        List<WorkflowDraftNode>? Nodes, List<WorkflowDraftConnection>? Connections, List<WorkflowDraftVariable>? Variables);

    public static bool TryParse(string draftDefinitionJson, out WorkflowDraftDefinition? definition, out string? parseError)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(draftDefinitionJson, SerializerOptions);
            if (envelope is null)
            {
                definition = null;
                parseError = "The draft definition is empty.";
                return false;
            }

            definition = new WorkflowDraftDefinition(
                envelope.InputsSchemaJson ?? "{}",
                envelope.OutputsSchemaJson ?? "{}",
                envelope.ErrorPolicyJson ?? "{}",
                envelope.ExecutionPolicyJson ?? "{}",
                envelope.SecurityPolicyJson ?? "{}",
                envelope.Nodes ?? [],
                envelope.Connections ?? [],
                envelope.Variables ?? []);
            parseError = null;
            return true;
        }
        catch (JsonException ex)
        {
            definition = null;
            parseError = $"The draft definition is not valid JSON: {ex.Message}";
            return false;
        }
    }
}
