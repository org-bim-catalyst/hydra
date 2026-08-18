using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Runtime;

/// <summary>
/// <see cref="IAgentPlanner"/> implementation. An invalid/unparseable model response triggers
/// one corrective retry (FR-037's retry budget) before throwing — the caller (<see
/// cref="AgentExecutionOrchestrator"/>) records that as an <see
/// cref="AgentExecutionErrorCategory.InvalidModelResponse"/> failure.
/// </summary>
public sealed class AgentPlanner(IAIProviderResolver providerResolver) : IAgentPlanner
{
    private const int MaxPlanningAttempts = 2;

    public async Task<AgentPlan> CreatePlanAsync(
        string objective,
        AgentInstructions instructions,
        IReadOnlyList<IAgentTool> availableTools,
        string providerKey,
        string modelKey,
        bool modelSupportsJsonMode,
        CancellationToken cancellationToken = default)
    {
        if (availableTools.Count == 0)
        {
            return new AgentPlan(objective, [new AgentPlanStep(0, "Generate a response to the objective.", AgentExecutionStepType.ModelReasoning, null)]);
        }

        var provider = providerResolver.Resolve(providerKey);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildPlanningSystemPrompt(instructions, availableTools)),
            new(ChatRole.User, objective),
        };
        var parameters = new GenerationParametersDto(JsonMode: modelSupportsJsonMode ? true : null);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxPlanningAttempts; attempt++)
        {
            var result = await provider.ChatAsync(messages, modelKey, parameters, cancellationToken);
            try
            {
                return ParsePlan(objective, result.Content, availableTools);
            }
            catch (JsonException ex)
            {
                lastError = ex;
                messages.Add(new ChatMessage(ChatRole.Assistant, result.Content));
                messages.Add(new ChatMessage(ChatRole.User, $"That response was invalid: {ex.Message} Respond again with only the JSON plan — no other text."));
            }
        }

        throw new InvalidOperationException($"The model did not return a valid plan after {MaxPlanningAttempts} attempts.", lastError);
    }

    private static string BuildPlanningSystemPrompt(AgentInstructions instructions, IReadOnlyList<IAgentTool> availableTools)
    {
        var toolDescriptions = string.Join(
            "\n", availableTools.Select(t => $"- {t.Name}: {t.Description} (input schema: {t.InputSchemaJson})"));

        const string jsonShape =
            """{"goal": "<restated goal>", "steps": [{"description": "<what this step does>", "toolName": "<exact tool name, or null for a reasoning-only step>", "dependsOnStepIndex": "<0-based index of a prior step this one requires, or null>"}]}""";

        return $"""
            {instructions.SystemInstructions}

            You are planning how to accomplish the user's objective. Available tools:
            {toolDescriptions}

            Respond with ONLY a JSON object of this exact shape, no other text, no markdown fence:
            {jsonShape}
            """;
    }

    private static AgentPlan ParsePlan(string objective, string content, IReadOnlyList<IAgentTool> availableTools)
    {
        var availableToolNames = availableTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(ExtractJsonObject(content));
        var root = document.RootElement;
        var goal = root.TryGetProperty("goal", out var goalElement) ? goalElement.GetString() ?? objective : objective;

        var steps = new List<AgentPlanStep>();
        var index = 0;
        foreach (var stepElement in root.GetProperty("steps").EnumerateArray())
        {
            var description = stepElement.GetProperty("description").GetString()
                ?? throw new JsonException("Each plan step requires a description.");
            var toolName = stepElement.TryGetProperty("toolName", out var toolNameElement) && toolNameElement.ValueKind == JsonValueKind.String
                ? toolNameElement.GetString()
                : null;

            if (toolName is not null && !availableToolNames.Contains(toolName))
            {
                throw new JsonException($"'{toolName}' is not one of this agent's configured tools.");
            }

            var dependsOnStepIndex = stepElement.TryGetProperty("dependsOnStepIndex", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Number
                ? dependsElement.GetInt32()
                : (int?)null;
            if (dependsOnStepIndex is not null && (dependsOnStepIndex < 0 || dependsOnStepIndex >= index))
            {
                throw new JsonException("dependsOnStepIndex must refer to an earlier step in the plan.");
            }

            var stepType = toolName is null ? AgentExecutionStepType.ModelReasoning : AgentExecutionStepType.ToolCall;

            steps.Add(new AgentPlanStep(index, description, stepType, toolName, dependsOnStepIndex));
            index++;
        }

        if (steps.Count == 0)
        {
            throw new JsonException("A plan must contain at least one step.");
        }

        return new AgentPlan(goal, steps);
    }

    /// <summary>Some providers wrap the JSON in prose or a markdown code fence despite instructions — take the outermost {...} span defensively, mirroring <c>MemoryExtractionJob.ExtractJsonArray</c>.</summary>
    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }
}
