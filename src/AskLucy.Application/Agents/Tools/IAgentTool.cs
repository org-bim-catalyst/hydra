using System.Text.Json;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

public enum AgentToolPermission
{
    ReadKnowledge,
    ReadMemory,
    ReadFile,
    WriteFile,
    ExternalNetwork,
    SendEmail,
    ExecuteCode,
    ModifyData,
    HighRiskOperation,

    // spec 021-mcp-integration (FR-021, research.md Decision 5) — additive; existing native-tool
    // values above are untouched. Covers requirements a native tool never had: acting on an
    // external, third-party system a tool call reaches through an MCP server.
    ReadExternalData,
    WriteExternalData,
    SendCommunication,
    ModifyExternalSystem,
    DeleteExternalData,
    ExecuteOperation,
}

/// <summary>Per-call context passed to every <see cref="IAgentTool"/> (contracts/agent-tool-contract.md). <see cref="UserChatId"/> is the execution's linked conversation, if any (FR-051/FR-052) — surfaced here so <see cref="ConversationTool"/> doesn't need its own repository round-trip just to resolve it.</summary>
public sealed record AgentToolExecutionContext(Guid ExecutionId, Guid StepId, string UserId, Guid AgentId, Guid AgentVersionId, Guid? UserChatId);

/// <summary>Result of one tool execution — exactly one of <see cref="Output"/>/<see cref="FailureReason"/> is set.</summary>
public sealed record AgentToolResult(bool Succeeded, JsonDocument? Output, string? FailureReason)
{
    public static AgentToolResult Success(JsonDocument output) => new(true, output, null);

    public static AgentToolResult Failure(string failureReason) => new(false, null, failureReason);
}

/// <summary>
/// The built-in tool abstraction (spec.md FR-020, contracts/agent-tool-contract.md, research.md
/// Decision 10). Every tool is a DI-registered class implementing this interface — input
/// validation, permission checks, the approval gate, output validation, and duplicate-call
/// detection are all enforced by the Agent Runtime around every call, never by the tool itself
/// (constitution §2.II OCP: a new tool is a new class, never an edit to the runtime).
/// <see cref="InputSchemaJson"/>/<see cref="OutputSchemaJson"/> are JSON Schema documents,
/// serialized as text — the same shape the planner (research.md Decision 11) shows the model.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    AgentToolRiskLevel RiskLevel { get; }

    IReadOnlyList<AgentToolPermission> RequiredPermissions { get; }

    string InputSchemaJson { get; }

    string OutputSchemaJson { get; }

    Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default);
}
