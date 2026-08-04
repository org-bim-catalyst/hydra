namespace AskLucy.Application.KnowledgeBases.Queries.ExportKnowledgeBase;

/// <summary>contracts/knowledge-bases-api.md `GET /{id}/export` JSON shape — `Folders` is a flat list (`KnowledgeBaseFolderDto` already carries `ParentFolderId`), the consumer rebuilds the tree.</summary>
public sealed record KnowledgeBaseExportDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<KnowledgeBaseFolderDto> Folders,
    int DocumentCount,
    int TotalPageCount,
    long StorageSizeBytes,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime ExportedAtUtc);
