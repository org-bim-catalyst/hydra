using System.Text.Json;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;

namespace AskLucy.Application.Agents.Tools;

// CA1711: not renamed — member names (not this type name) are round-tripped through
// Enum.TryParse against persisted data (McpTool.RequiredPermissionsJson via
// McpToolAdapter.ParsePermissions) and this type is referenced across native tools and the
// Application.Tests project, which are outside this cleanup's Application/Domain scope, so a
// rename here cannot safely update every reference in the same change.
#pragma warning disable CA1711
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
#pragma warning restore CA1711

/// <summary>Per-call context passed to every <see cref="IAgentTool"/> (contracts/agent-tool-contract.md). <see cref="UserChatId"/> is the execution's linked conversation, if any (FR-051/FR-052) — surfaced here so <see cref="ConversationTool"/> doesn't need its own repository round-trip just to resolve it.</summary>
public sealed record AgentToolExecutionContext(Guid ExecutionId, Guid StepId, string UserId, Guid AgentId, Guid AgentVersionId, Guid? UserChatId);

/// <summary>
/// Result of one tool execution. A success always carries <see cref="Output"/> and no
/// <see cref="FailureReason"/>; a failure always carries a <see cref="FailureReason"/>.
/// <para>
/// A failure may <b>additionally</b> carry an <see cref="Output"/> payload when the capability
/// still has something the viewer can act on even though its own job did not complete — the site
/// boundary lookup knowing exactly where the site is but being unable to draw its outline, for
/// instance. That payload never reaches the narration or the background agent runtime as a result:
/// only the conversational viewer-payload extractor reads it, and only for the viewer commands it
/// recognises. The failure itself stays a failure — nothing here may be read as "it worked".
/// </para>
/// </summary>
public sealed record AgentToolResult(bool Succeeded, JsonDocument? Output, string? FailureReason)
{
    public static AgentToolResult Success(JsonDocument output) => new(true, output, null);

    public static AgentToolResult Failure(string failureReason) => new(false, null, failureReason);

    /// <summary>
    /// A failure that still gives the viewer something to do. See the type remarks — the failure
    /// reason is what gets narrated; <paramref name="viewerPayload"/> is not reported as a result.
    /// </summary>
    public static AgentToolResult FailureWithViewerPayload(string failureReason, JsonDocument viewerPayload) =>
        new(false, viewerPayload, failureReason);
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
