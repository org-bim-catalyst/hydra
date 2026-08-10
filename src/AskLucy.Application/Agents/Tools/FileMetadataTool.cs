using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>Reads a document's metadata only, never its content (spec.md FR-024) — the read-only-metadata counterpart to <see cref="FileReadTool"/>.</summary>
public sealed class FileMetadataTool(IDocumentRepository documentRepository) : IAgentTool
{
    public string Name => "FileMetadataTool";

    public string Description => "Reads metadata (file name, type, size, page count) for one of the caller's own documents, without reading its content.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadFile];

    public string InputSchemaJson => """{"type":"object","required":["documentId"],"properties":{"documentId":{"type":"string"}}}""";

    public string OutputSchemaJson => """{"type":"object","properties":{"fileName":{"type":"string"},"fileType":{"type":"string"},"sizeBytes":{"type":"integer"},"pageCount":{"type":["integer","null"]}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("documentId", out var idElement) || !Guid.TryParse(idElement.GetString(), out var documentId))
        {
            return AgentToolResult.Failure("A valid documentId is required.");
        }

        var document = DocumentOwnershipGuard.EnsureOwnedBy(await documentRepository.GetByIdAsync(documentId, cancellationToken), context.UserId);
        var version = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            fileName = document.FileName,
            fileType = document.FileType.ToString(),
            sizeBytes = document.SizeBytes,
            pageCount = version.PageCount,
        }));
    }
}
