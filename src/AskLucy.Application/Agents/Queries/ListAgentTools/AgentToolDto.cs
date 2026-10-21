using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Queries.ListAgentTools;

/// <summary>
/// One row of the admin AI Tools view — a tool the platform's agents can call.
///
/// <para>
/// The schemas are carried as the raw JSON the tool itself publishes, not parsed and re-emitted:
/// the tool owns that document, and a re-serialization here would quietly change what the
/// platform tells a model it may send.
/// </para>
/// </summary>
public sealed record AgentToolDto(
    string Name,
    string Description,
    AgentToolRiskLevel RiskLevel,
    IReadOnlyList<string> RequiredPermissions,
    string InputSchemaJson,
    string OutputSchemaJson)
{
    public static AgentToolDto Create(IAgentTool tool) => new(
        tool.Name,
        tool.Description,
        tool.RiskLevel,
        [.. tool.RequiredPermissions.Select(p => p.ToString())],
        tool.InputSchemaJson,
        tool.OutputSchemaJson);
}
