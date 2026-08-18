using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Resolves a reusable prompt's content for the agent's own reasoning to use as context (spec.md
/// FR-024/FR-033). Deliberately does <em>not</em> delegate to <c>ExecutePromptCommand</c>
/// (research.md Decision 6's original assumption) — that handler depends on
/// <c>ICurrentUserAccessor</c>, which never resolves inside the background orchestrator (no HTTP
/// context, same constraint as <see cref="AgentExecutionOrchestrator"/>'s own doc comment), and it
/// is the Prompt Testing Workspace's own AI-call-and-persist flow, not "give me this prompt's
/// resolved text." Instead reuses <see cref="IPromptRepository"/> and the existing, pure
/// <see cref="PromptVariableResolver"/> directly — no new prompt storage/templating logic
/// (FR-033) — and returns the resolved text; the tool never calls an AI provider itself.
/// </summary>
public sealed class PromptExecutionTool(IPromptRepository promptRepository) : IAgentTool
{
    public string Name => "PromptExecutionTool";

    public string Description => "Resolves one of the caller's saved reusable prompts (with variables substituted) into text the agent can use.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson => """{"type":"object","required":["promptId"],"properties":{"promptId":{"type":"string"},"variableValues":{"type":"object"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"resolvedText":{"type":"string"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("promptId", out var promptIdElement) || !Guid.TryParse(promptIdElement.GetString(), out var promptId))
        {
            return AgentToolResult.Failure("A valid promptId is required.");
        }

        var prompt = await promptRepository.GetByIdForOwnerAsync(promptId, context.UserId, cancellationToken);
        if (prompt is null)
        {
            return AgentToolResult.Failure("Prompt not found.");
        }

        var version = await promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var suppliedValues = input.RootElement.TryGetProperty("variableValues", out var valuesElement)
            ? valuesElement.EnumerateObject().ToDictionary(p => p.Name, p => (string?)p.Value.GetString())
            : new Dictionary<string, string?>();

        var resolution = PromptVariableResolver.ValidateAndResolve(version.Variables, suppliedValues);
        if (!resolution.IsValid)
        {
            return AgentToolResult.Failure($"Prompt variable resolution failed: {string.Join("; ", resolution.Errors.Select(e => e.Message))}");
        }

        var segments = new[] { version.SystemInstructions, version.DeveloperInstructions, version.UserInstructions }
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => PromptVariableResolver.ResolveContent(s!, resolution.ResolvedValues));

        var resolvedText = string.Join("\n\n", segments);

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { resolvedText }));
    }
}
