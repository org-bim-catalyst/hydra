using System.Security.Cryptography;
using System.Text;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Conversations.SystemAgents;

/// <summary>
/// A platform-provisioned agent's whole definition, as a versioned artifact (specs/045
/// contracts/system-agent-provisioning.md §1, constitution §9 — "prompts are versioned artifacts,
/// reviewed like code" applied to a whole agent, not just its prompt text).
/// </summary>
/// <param name="SystemKey">Stable identity <see cref="Agent.SystemKey"/> upserts on — never renamed across a release.</param>
/// <param name="CapabilityKeys">
/// The capability keys this agent's version snapshots (contracts/system-agent-provisioning.md
/// §1's table) — never validated against <c>ConversationCapabilityCatalog</c> at provisioning
/// time, since capabilities are registered per request scope and provisioning runs once at
/// startup outside any conversation; the list is a documentation/audit snapshot of intent, not a
/// live binding a turn resolves through.
/// </param>
public sealed record SystemAgentDefinition(
    string SystemKey,
    string Name,
    string Description,
    AgentType AgentType,
    AiCapability ModelCapability,
    AgentInstructions Instructions,
    IReadOnlyList<string> CapabilityKeys,
    AgentExecutionPolicy ExecutionPolicy)
{
    /// <summary>
    /// SHA-256 over every field above, hex-encoded. Gates re-publishing (FR-035): the provisioner
    /// only writes a new <see cref="AgentVersion"/> when this differs from the newest one's
    /// <see cref="AgentVersion.DefinitionHash"/>, so restarting with no code change performs zero
    /// writes.
    /// </summary>
    public string ComputeHash()
    {
        const char separator = '\u001F';
        var payload = string.Join(separator,
            SystemKey, Name, Description, AgentType.ToString(), ModelCapability.ToString(),
            Instructions.SystemInstructions, Instructions.Objectives, Instructions.Constraints,
            Instructions.BehavioralRules, Instructions.OutputRequirements, Instructions.ToolUsageRules, Instructions.SafetyRules,
            string.Join(',', CapabilityKeys),
            ExecutionPolicy.MaxSteps, ExecutionPolicy.MaxExecutionDurationSeconds, ExecutionPolicy.MaxTokens,
            ExecutionPolicy.MaxCost, ExecutionPolicy.MaxToolCalls, ExecutionPolicy.MaxRetries);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
