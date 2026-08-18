using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Reads a document's text content (spec.md FR-024, research.md Decision 7) — reuses the
/// existing <c>DocumentOwnershipGuard</c> (404, never 403) and the current version's already-
/// extracted text where available; falls back to reading raw bytes via <see cref="IFileStorage"/>
/// (decoded as UTF-8) only when no extraction exists yet. Physical storage paths are never
/// exposed to the tool's output.
/// </summary>
public sealed class FileReadTool(IDocumentRepository documentRepository, IFileStorage fileStorage) : IAgentTool
{
    public string Name => "FileReadTool";

    public string Description => "Reads the text content of one of the caller's own documents.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadFile];

    public string InputSchemaJson => """{"type":"object","required":["documentId"],"properties":{"documentId":{"type":"string"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"content":{"type":"string"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("documentId", out var idElement) || !Guid.TryParse(idElement.GetString(), out var documentId))
        {
            return AgentToolResult.Failure("A valid documentId is required.");
        }

        var document = DocumentOwnershipGuard.EnsureOwnedBy(await documentRepository.GetByIdAsync(documentId, cancellationToken), context.UserId);
        var version = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        string content;
        if (!string.IsNullOrEmpty(version.ExtractedText))
        {
            content = version.ExtractedText;
        }
        else
        {
            await using var stream = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
            using var reader = new StreamReader(stream);
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { content }));
    }
}
