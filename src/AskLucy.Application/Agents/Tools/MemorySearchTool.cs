using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>Read-only memory retrieval (spec.md FR-024/FR-030, research.md Decision 4) — exclusively through the existing <see cref="IMemoryService"/> abstraction, never a new memory implementation.</summary>
public sealed class MemorySearchTool(IMemoryService memoryService) : IAgentTool
{
    public string Name => "MemorySearchTool";

    public string Description => "Retrieves the caller's relevant long-term memories for a query.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadMemory];

    public string InputSchemaJson => """{"type":"object","required":["query"],"properties":{"query":{"type":"string"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"contextText":{"type":"string"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) || queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty query is required.");
        }

        var outcome = await memoryService.RetrieveRelevantMemoriesAsync(context.UserId, Guid.CreateVersion7(), projectId: null, query, cancellationToken);

        return outcome.Type switch
        {
            MemoryRetrievalOutcomeType.Found => AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = outcome.ContextText })),
            MemoryRetrievalOutcomeType.NoneRelevant => AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = (string?)null })),
            _ => AgentToolResult.Failure(outcome.UnavailableReason ?? "Memory search is temporarily unavailable."),
        };
    }
}
