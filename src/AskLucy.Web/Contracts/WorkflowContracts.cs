using AskLucy.Domain.Workflows;

namespace AskLucy.Web.Contracts;

public sealed record CreateWorkflowRequest(string Name, string? Description, WorkflowType WorkflowType, string? EventTriggerConfigurationJson = null);

public sealed record UpdateWorkflowRequest(string Name, string? Description, string DraftDefinitionJson, string? EventTriggerConfigurationJson = null);

public sealed record PublishWorkflowVersionRequest(string? ChangeDescription);

public sealed record StartWorkflowExecutionRequest(Guid WorkflowId, int? WorkflowVersionNumber, string InputsJson, WorkflowExecutionTriggerType TriggerType = WorkflowExecutionTriggerType.Manual);

public sealed record RejectWorkflowNodeRequest(string? Reason);

public sealed record RequestWorkflowNodeChangesRequest(string Comments);

public sealed record CreateWorkflowPolicyRequest(string Name, string? Description, WorkflowNodeType? WorkflowNodeType, string? UnderlyingToolName, string? ConditionsJson);

public sealed record UpdateWorkflowPolicyRequest(string Name, string? Description, string? ConditionsJson, bool IsEnabled);

public sealed record SetWorkflowUserExecutionLimitRequest(int MaxConcurrentExecutions);
