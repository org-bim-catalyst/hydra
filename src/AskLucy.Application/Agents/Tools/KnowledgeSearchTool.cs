using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Semantic search over the agent's configured Knowledge Bases (spec.md FR-024/FR-029,
/// research.md Decision 4). Exclusively through the existing <see cref="IRagService"/>
/// abstraction — never a new retrieval/vector-search implementation (FR-029). Knowledge Base
/// access is the intersection of the agent's configuration and the executing user's own
/// authorization (FR-049), enforced via <see cref="IKnowledgeBaseRepository.ResolveOwnedIdsAsync"/>,
/// exactly the pattern the Knowledge Base Engine itself already uses.
/// </summary>
public sealed class KnowledgeSearchTool(IRagService ragService, IKnowledgeBaseRepository knowledgeBaseRepository) : IAgentTool
{
    public string Name => "KnowledgeSearchTool";

    public string Description => "Searches the agent's configured Knowledge Bases for content relevant to a query, returning grounded context and citations.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadKnowledge];

    public string InputSchemaJson => """{"type":"object","required":["query","knowledgeBaseIds"],"properties":{"query":{"type":"string"},"knowledgeBaseIds":{"type":"array","items":{"type":"string"}}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"contextText":{"type":"string"},"citations":{"type":"array"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) || queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty query is required.");
        }

        var requestedIds = input.RootElement.TryGetProperty("knowledgeBaseIds", out var idsElement)
            ? idsElement.EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).ToList()
            : null;

        // FR-049 — the effective set is always the intersection of what's requested/configured
        // and what the executing user is independently authorized for, never broader.
        var authorizedIds = await knowledgeBaseRepository.ResolveOwnedIdsAsync(context.UserId, requestedIds, excludeIds: null, cancellationToken);
        if (authorizedIds.Count == 0)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = (string?)null, citations = Array.Empty<object>() }));
        }

        var outcome = await ragService.RetrieveContextAsync(Guid.CreateVersion7(), query, authorizedIds, cancellationToken);

        return outcome.Type switch
        {
            RagRetrievalOutcomeType.Grounded => AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                contextText = outcome.ContextText,
                citations = outcome.Citations.Select(c => new
                {
                    documentTitle = c.DocumentTitle,
                    knowledgeBaseName = c.KnowledgeBaseName,
                    pageNumber = c.PageNumber,
                    section = c.Section,
                    excerpt = c.Excerpt,
                }),
            })),
            RagRetrievalOutcomeType.NoRelevantContent => AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { contextText = (string?)null, citations = Array.Empty<object>() })),
            _ => AgentToolResult.Failure(outcome.UnavailableReason ?? "Knowledge search is temporarily unavailable."),
        };
    }
}
