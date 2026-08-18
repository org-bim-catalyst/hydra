using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Commands.UpdateAgent;
using AskLucy.Domain.Agents;

namespace AskLucy.Web.Contracts;

public sealed record CreateAgentRequest(
    string Name,
    string? Description,
    AgentType AgentType,
    AgentInstructionsDto Instructions,
    Guid? ModelProviderId,
    Guid? ModelId,
    AgentOutputFormat OutputFormat,
    AgentExecutionPolicyDto ExecutionPolicy);

public sealed record UpdateAgentRequest(
    string Name,
    string? Description,
    AgentType AgentType,
    AgentInstructionsDto Instructions,
    Guid? ModelProviderId,
    Guid? ModelId,
    AgentOutputFormat OutputFormat,
    AgentExecutionPolicyDto ExecutionPolicy,
    IReadOnlyList<AgentToolInput>? Tools = null,
    IReadOnlyList<Guid>? KnowledgeBaseIds = null,
    AgentMemoryPolicyInput? MemoryPolicy = null);

public sealed record PublishAgentVersionRequest(string? ChangeDescription);

public sealed record StartAgentExecutionRequest(
    Guid AgentId,
    int? AgentVersionNumber,
    string Objective,
    AgentConversationIntegrationMode ConversationIntegrationMode,
    Guid? UserChatId,
    bool IsTestExecution = false);

public sealed record RejectAgentActionRequest(string? Reason);

public sealed record CreateAgentPolicyRequest(string Name, string? Description, string ToolName, string? ConditionsJson);

public sealed record UpdateAgentPolicyRequest(string Name, string? Description, string? ConditionsJson, bool IsEnabled);

public sealed record SetAgentUserExecutionLimitRequest(int MaxConcurrentExecutions);
