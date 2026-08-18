using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>Read-only access to the linked conversation's recent messages (spec.md FR-024) — a no-op (empty result) for a Standalone execution with no linked conversation.</summary>
public sealed class ConversationTool(IMessageRepository messageRepository) : IAgentTool
{
    public string Name => "ConversationTool";

    public string Description => "Reads the most recent messages from the conversation this execution is linked to, if any.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson => """{"type":"object","properties":{"maxMessages":{"type":"integer"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"messages":{"type":"array"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (context.UserChatId is not { } userChatId)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { messages = Array.Empty<object>() }));
        }

        var maxMessages = input.RootElement.TryGetProperty("maxMessages", out var maxElement) ? maxElement.GetInt32() : 20;

        var messages = await messageRepository.ListByChatIdAsync(userChatId, cancellationToken);
        var recent = messages
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(Math.Clamp(maxMessages, 1, 100))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new { role = m.Role.ToString(), content = m.Content })
            .ToList();

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { messages = recent }));
    }
}
