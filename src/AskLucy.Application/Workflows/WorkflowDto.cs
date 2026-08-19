using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows;

public sealed record WorkflowDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string WorkflowType,
    string Status,
    string DraftDefinitionJson,
    int? PublishedVersionNumber,
    string? EventTriggerConfigurationJson,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc)
{
    public static WorkflowDetailDto Create(Workflow workflow) => new(
        workflow.Id, workflow.Name, workflow.Description, workflow.WorkflowType.ToString(), workflow.Status.ToString(),
        workflow.DraftDefinitionJson, workflow.PublishedVersionNumber, workflow.EventTriggerConfigurationJson,
        workflow.CreatedAtUtc, workflow.ModifiedAtUtc);
}

public sealed record WorkflowListItemDto(
    Guid Id, string Name, string? Description, string WorkflowType, string Status, int? PublishedVersionNumber, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc)
{
    public static WorkflowListItemDto Create(Workflow workflow) => new(
        workflow.Id, workflow.Name, workflow.Description, workflow.WorkflowType.ToString(), workflow.Status.ToString(),
        workflow.PublishedVersionNumber, workflow.CreatedAtUtc, workflow.ModifiedAtUtc);
}

public sealed record WorkflowNodeDto(
    Guid Id, string NodeKey, string NodeType, string Name, string? Description, string ConfigurationJson,
    int? TimeoutSeconds, string ApprovalPolicy, double CanvasX, double CanvasY)
{
    public static WorkflowNodeDto Create(WorkflowNode node) => new(
        node.Id, node.NodeKey, node.NodeType.ToString(), node.Name, node.Description, node.ConfigurationJson,
        node.TimeoutSeconds, node.ApprovalPolicy.ToString(), node.CanvasX, node.CanvasY);
}

public sealed record WorkflowConnectionDto(Guid Id, Guid SourceNodeId, Guid TargetNodeId, string? BranchLabel)
{
    public static WorkflowConnectionDto Create(WorkflowConnection connection) => new(
        connection.Id, connection.SourceNodeId, connection.TargetNodeId, connection.BranchLabel);
}

public sealed record WorkflowVersionDto(
    Guid Id,
    Guid WorkflowId,
    int VersionNumber,
    string InputsSchemaJson,
    string OutputsSchemaJson,
    string ExecutionPolicyJson,
    string? ChangeDescription,
    string PublishedBy,
    DateTime CreatedAtUtc,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowConnectionDto> Connections)
{
    public static WorkflowVersionDto Create(WorkflowVersion version) => new(
        version.Id, version.WorkflowId, version.VersionNumber, version.InputsSchemaJson, version.OutputsSchemaJson,
        version.ExecutionPolicyJson, version.ChangeDescription, version.PublishedBy, version.CreatedAtUtc,
        version.Nodes.Select(WorkflowNodeDto.Create).ToList(), version.Connections.Select(WorkflowConnectionDto.Create).ToList());
}

/// <summary>One <see cref="Validation.WorkflowValidationIssue"/> serialized for the API (contracts/workflows-api.md's <c>ValidateWorkflowCommand</c>).</summary>
public sealed record WorkflowValidationIssueDto(string? NodeKey, string Message);
