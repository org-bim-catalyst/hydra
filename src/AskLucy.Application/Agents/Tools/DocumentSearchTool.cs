using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>Searches the caller's own document library by filename/extracted text/metadata (spec.md FR-024) — reuses <see cref="IDocumentRepository.SearchAsync"/> verbatim, no new search logic.</summary>
public sealed class DocumentSearchTool(IDocumentRepository documentRepository) : IAgentTool
{
    public string Name => "DocumentSearchTool";

    public string Description => "Searches the caller's own document library for documents matching a query.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadFile];

    public string InputSchemaJson => """{"type":"object","required":["query"],"properties":{"query":{"type":"string"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"documents":{"type":"array"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) || queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty query is required.");
        }

        var (items, _) = await documentRepository.SearchAsync(
            context.UserId, DocumentListView.Active, folderId: null, new DocumentSearchFilters(Query: query), cursor: null, pageSize: 10, cancellationToken);

        var results = items.Select(d => new { id = d.Id, fileName = d.FileName, fileType = d.FileType.ToString() });

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { documents = results }));
    }
}
